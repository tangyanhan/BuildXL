// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Native.IO;
using BuildXL.Native.IO.Windows;
using BuildXL.Storage.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using Microsoft.Win32.SafeHandles;

namespace BuildXL.Storage
{
    /// <summary>
    /// Map local volumes, allowing lookup of volume path by serial number and opening files by ID.
    /// </summary>
    /// <remarks>
    /// X:\ marks the spot.
    /// </remarks>
    public sealed class VolumeMap
    {
        private readonly Dictionary<ulong, VolumeGuidPath> m_volumePathsBySerial = new Dictionary<ulong, VolumeGuidPath>();
        private readonly Dictionary<string, FileIdAndVolumeId> m_junctionRootFileIds = new Dictionary<string, FileIdAndVolumeId>();
        private readonly Dictionary<string, FileIdAndVolumeId> m_gvfsProjections = new Dictionary<string, FileIdAndVolumeId>();

        private readonly List<string> m_unchangedJunctionRoots = new List<string>();
        private readonly List<string> m_changedGvfsProjections = new List<string>();

        /// <summary>
        /// Unchanged junction roots
        /// </summary>
        public IReadOnlyList<string> UnchangedJunctionRoots => m_unchangedJunctionRoots;

        /// <summary>
        /// All GVFS_projection files found in all readable mounts.
        /// </summary>
        public IReadOnlyCollection<string> GvfsProjections => m_gvfsProjections.Keys;

        /// <summary>
        /// Roots of GVFS repos whose file projections changed
        /// </summary>
        public IReadOnlyList<string> ChangedGvfsProjections => m_changedGvfsProjections;

        /// <summary>
        /// Hooks used by unit tests to skip tracking volumes that do not have journal capability.
        /// </summary>
        public bool SkipTrackingJournalIncapableVolume { get; set; }

        private VolumeMap()
        {
        }

        /// <summary>
        /// Creates a map of local volumes. In the event of a collision which prevents constructing a serial -> path mapping,
        /// a warning is logged and those volumes are excluded from the map. On failure, returns null.
        /// </summary>
        public static VolumeMap CreateMapOfAllLocalVolumes(
            LoggingContext loggingContext,
            IReadOnlyList<string> junctionRoots = null,
            IReadOnlyList<string> gvfsProjectionFiles = null)
        {
            var map = new VolumeMap();

            var guidPaths = new HashSet<VolumeGuidPath>();
            List<Tuple<VolumeGuidPath, ulong>> volumes = FileUtilities.ListVolumeGuidPathsAndSerials();
            foreach (var volume in volumes)
            {
                bool guidPathUnique = guidPaths.Add(volume.Item1);
                Contract.Assume(guidPathUnique, "Duplicate guid path");

                VolumeGuidPath collidedGuidPath;
                if (map.m_volumePathsBySerial.TryGetValue(volume.Item2, out collidedGuidPath))
                {
                    if (collidedGuidPath.IsValid)
                    {
                        // This could be an error. Instead, we optimistically create a partial map and hope that theese volumes are not relevant to the build.
                        // Some users have reported VHD-creation automation (concurrent with BuildXL) causing a collision.
                        Logger.Log.StorageVolumeCollision(loggingContext, volume.Item2, volume.Item1.Path, collidedGuidPath.Path);

                        // Poison this entry so that we know it is unusable on lookup (ambiguous)
                        map.m_volumePathsBySerial[volume.Item2] = VolumeGuidPath.Invalid;
                    }
                }
                else
                {
                    map.m_volumePathsBySerial.Add(volume.Item2, volume.Item1);
                }
            }

            toDictionary(junctionRoots, map.m_junctionRootFileIds);
            toDictionary(gvfsProjectionFiles, map.m_gvfsProjections);

            return map;

            void toDictionary(IEnumerable<string> paths, Dictionary<string, FileIdAndVolumeId> result)
            {
                if (paths != null)
                {
                    foreach (var pathStr in paths)
                    {
                        FileIdAndVolumeId? id = TryGetFinalFileIdAndVolumeId(pathStr);
                        if (id.HasValue)
                        {
                            result[pathStr] = id.Value;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Gets all (volume serial, volume guid path) pairs in this map.
        /// </summary>
        public IEnumerable<KeyValuePair<ulong, VolumeGuidPath>> Volumes =>
            // Exclude collision markers from enumeration of valid volumes.
            m_volumePathsBySerial.Where(kvp => kvp.Value.IsValid);

        /// <summary>
        /// Validate junction roots
        /// </summary>
        public void ValidateJunctionRoots(LoggingContext loggingContext, VolumeMap previousMap)
        {
            m_unchangedJunctionRoots.Clear();
            m_changedGvfsProjections.Clear();

            foreach (var junction in m_junctionRootFileIds)
            {
                string path = junction.Key;
                var result = compareToPrevious(path, junction.Value, previousMap.m_junctionRootFileIds);
                Logger.Log.ValidateJunctionRoot(loggingContext, path, result.ToString());
                if (result == PathValidationResult.Unchanged)
                {
                    m_unchangedJunctionRoots.Add(path);
                }
            }

            foreach (var gvfsProjection in m_gvfsProjections)
            {
                string gvfsProjectionFile = gvfsProjection.Key;
                var result = compareToPrevious(gvfsProjectionFile, gvfsProjection.Value, previousMap.m_gvfsProjections);
                Logger.Log.ValidateJunctionRoot(loggingContext, gvfsProjectionFile, result.ToString());
                if (result != PathValidationResult.Unchanged)
                {
                    m_changedGvfsProjections.Add(gvfsProjectionFile);
                }
            }

            PathValidationResult compareToPrevious(string path, FileIdAndVolumeId id, Dictionary<string, FileIdAndVolumeId> previousMap)
            {
                var previousFound = previousMap.TryGetValue(path, out var previousId);
                return
                    previousFound && previousId == id ? PathValidationResult.Unchanged :
                    previousFound && previousId != id ? PathValidationResult.Changed :
                    PathValidationResult.NewlyCreated;
            }
        }

        /// <summary>
        /// Looks up the GUID path that corresponds to the given serial. Returns <see cref="VolumeGuidPath.Invalid"/> if there is no volume with the given
        /// serial locally.
        /// </summary>
        /// <remarks>
        /// The serial should have 64 significant bits when available on Windows 8.1+ (i.e., the serial returned by <c>GetVolumeInformation</c>
        /// is insufficient). The appropriate serial can be retrieved from any handle on the volume via <see cref="FileUtilities.GetVolumeSerialNumberByHandle"/>.
        /// </remarks>
        public VolumeGuidPath TryGetVolumePathBySerial(ulong volumeSerial)
        {
            VolumeGuidPath maybePath;
            Analysis.IgnoreResult(m_volumePathsBySerial.TryGetValue(volumeSerial, out maybePath));

            // Note that maybePath may be Invalid even if found; we store an Invalid guid path to mark a volume-serial collision, and exclude those volumes from the map.
            return maybePath;
        }

        /// <summary>
        /// Looks up the GUID path for the volume containing the given file handle. Returns <see cref="VolumeGuidPath.Invalid"/> if a matching volume cannot be found
        /// (though that suggests that this volume map is incomplete).
        /// </summary>
        public VolumeGuidPath TryGetVolumePathForHandle(SafeFileHandle handle)
        {
            Contract.Requires(handle != null);

            return TryGetVolumePathBySerial(FileUtilities.GetVolumeSerialNumberByHandle(handle));
        }

        /// <summary>
        /// Creates a <see cref="FileAccessor"/> which can open files based on this volume map.
        /// </summary>
        public FileAccessor CreateFileAccessor()
        {
            return new FileAccessor(this);
        }

        /// <summary>
        /// Creates a <see cref="VolumeAccessor"/> which can open volume handles based on this volume map.
        /// </summary>
        public VolumeAccessor CreateVolumeAccessor()
        {
            return new VolumeAccessor(this);
        }

        /// <summary>
        /// Serializes this instance of <see cref="VolumeMap"/>.
        /// </summary>
        public void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);
            writer.WriteCompact(m_volumePathsBySerial.Count);

            foreach (var volumeGuidPath in m_volumePathsBySerial)
            {
                writer.Write(volumeGuidPath.Key);
                if (volumeGuidPath.Value.IsValid)
                {
                    writer.Write(true);
                    writer.Write(volumeGuidPath.Value.Path);
                }
                else
                {
                    writer.Write(false);
                }
            }

            WriteDictionary(writer, m_junctionRootFileIds);
            WriteDictionary(writer, m_gvfsProjections);
        }

        /// <summary>
        /// Deserializes into an instance of <see cref="VolumeMap"/>.
        /// </summary>
        public static VolumeMap Deserialize(BuildXLReader reader)
        {
            Contract.Requires(reader != null);

            var volumeMap = new VolumeMap();
            int count = reader.ReadInt32Compact();

            for (int i = 0; i < count; ++i)
            {
                ulong serialNumber = reader.ReadUInt64();
                bool isValid = reader.ReadBoolean();
                VolumeGuidPath path = isValid ? VolumeGuidPath.Create(reader.ReadString()) : VolumeGuidPath.Invalid;
                volumeMap.m_volumePathsBySerial.Add(serialNumber, path);
            }

            ReadDictionary(reader, volumeMap.m_junctionRootFileIds);
            ReadDictionary(reader, volumeMap.m_gvfsProjections);

            return volumeMap;
        }

        private static void WriteDictionary(BuildXLWriter writer, Dictionary<string, FileIdAndVolumeId> dict)
        {
            writer.WriteCompact(dict.Count);
            foreach (var kvp in dict)
            {
                writer.Write(kvp.Key);
                kvp.Value.Serialize(writer);
            }
        }

        private static void ReadDictionary(BuildXLReader reader, Dictionary<string, FileIdAndVolumeId> dict)
        {
            int count = reader.ReadInt32Compact();
            for (int i = 0; i < count; ++i)
            {
                string path = reader.ReadString();
                var id = FileIdAndVolumeId.Deserialize(reader);
                dict.Add(path, id);
            }
        }

        private static FileIdAndVolumeId? TryGetFinalFileIdAndVolumeId(string path)
        {
            SafeFileHandle handle = null;
            var openResult = FileUtilities.TryOpenDirectory(
                path,
                FileDesiredAccess.None,
                FileShare.Read | FileShare.Write | FileShare.Delete,
                // Don't open with FileFlagsAndAttributes.FileFlagOpenReparsePoint because we want to get to the final file id and volume id.
                FileFlagsAndAttributes.FileFlagBackupSemantics,
                out handle);

            if (!openResult.Succeeded)
            {
                return null;
            }

            using (handle)
            {
                return FileUtilities.TryGetFileIdAndVolumeIdByHandle(handle);
            }
        }

        /// <summary>
        /// Result of path validation.
        /// </summary>
        private enum PathValidationResult
        {
            /// <summary>
            /// The path is unchanged, i.e., the corresponding file still has the same volume/file id.
            /// </summary>
            Unchanged,

            /// <summary>
            /// The path has changed, i.e., the same path now corresponds to a file with a different volume/file id.
            /// </summary>
            Changed,

            /// <summary>
            /// The path did not previously exist but it exists now.
            /// </summary>
            NewlyCreated
        }
    }

    /// <summary>
    /// Allows opening a batch of files based on their <see cref="FileId"/> and volume serial number.
    /// </summary>
    /// <remarks>
    /// Unlike the <see cref="VolumeMap"/> upon which it operates, this class is not thread-safe.
    /// This class is disposable since it holds handles to volume root paths. At most, it holds one handle to each volume.
    /// </remarks>
    public sealed class FileAccessor : IDisposable
    {
        private Dictionary<ulong, SafeFileHandle> m_volumeRootHandles = new Dictionary<ulong, SafeFileHandle>();
        private readonly VolumeMap m_map;

        internal FileAccessor(VolumeMap map)
        {
            Contract.Requires(map != null);
            m_map = map;
            Disposed = false;
        }

        /// <summary>
        /// Error reasons for <see cref="FileAccessor.TryOpenFileById"/>
        /// </summary>
        public enum OpenFileByIdResult : byte
        {
            /// <summary>
            /// Opened a handle.
            /// </summary>
            Succeeded = 0,

            /// <summary>
            /// The containing volume could not be opened.
            /// </summary>
            FailedToOpenVolume = 1,

            /// <summary>
            /// The given file ID does not exist on the volume.
            /// </summary>
            FailedToFindFile = 2,

            /// <summary>
            /// The file ID exists on the volume but could not be opened
            /// (due to permissions, a sharing violation, a pending deletion, etc.)
            /// </summary>
            FailedToAccessExistentFile = 3,
        }

        /// <summary>
        /// Tries to open a handle to the given file as identified by a (<paramref name="volumeSerial"/>, <paramref name="fileId"/>) pair.
        /// If the result is <see cref="OpenFileByIdResult.Succeeded"/>, <paramref name="fileHandle"/> has been set to a valid handle.
        /// </summary>
        /// <remarks>
        /// This method is not thread safe (see <see cref="FileAccessor"/> remarks).
        /// </remarks>
        public OpenFileByIdResult TryOpenFileById(
            ulong volumeSerial,
            FileId fileId,
            FileDesiredAccess desiredAccess,
            FileShare shareMode,
            FileFlagsAndAttributes flagsAndAttributes,
            out SafeFileHandle fileHandle)
        {
            Contract.Requires(!Disposed);

            SafeFileHandle volumeRootHandle = TryGetVolumeRoot(volumeSerial);

            if (volumeRootHandle == null)
            {
                fileHandle = null;
                return OpenFileByIdResult.FailedToOpenVolume;
            }

            OpenFileResult openResult = FileUtilities.TryOpenFileById(
                volumeRootHandle,
                fileId,
                desiredAccess,
                shareMode,
                flagsAndAttributes,
                out fileHandle);

            if (!openResult.Succeeded)
            {
                Contract.Assert(fileHandle == null);

                if (openResult.Status.IsNonexistent())
                {
                    return OpenFileByIdResult.FailedToFindFile;
                }
                else
                {
                    return OpenFileByIdResult.FailedToAccessExistentFile;
                }
            }

            return OpenFileByIdResult.Succeeded;
        }

        /// <summary>
        /// Indicates if this instance has been disposed via <see cref="Dispose"/>.
        /// </summary>
        /// <remarks>
        /// Needs to be public for contract preconditions.
        /// </remarks>
        public bool Disposed { get; private set; }

        /// <summary>
        /// Closes any handles held by this instance.
        /// </summary>
        public void Dispose()
        {
            if (Disposed)
            {
                return;
            }

            foreach (SafeFileHandle handle in m_volumeRootHandles.Values)
            {
                handle.Dispose();
            }

            m_volumeRootHandles = null;

            Disposed = true;
        }

        /// <summary>
        /// Creates a volume root handle or retrieves an existing one.
        /// </summary>
        /// <remarks>
        /// This is the un-synchronized get-or-add operation resulting in <see cref="FileAccessor"/>
        /// not being thread-safe.
        /// </remarks>
        private SafeFileHandle TryGetVolumeRoot(ulong volumeSerial)
        {
            SafeFileHandle volumeRootHandle;
            if (!m_volumeRootHandles.TryGetValue(volumeSerial, out volumeRootHandle))
            {
                VolumeGuidPath volumeRootPath = m_map.TryGetVolumePathBySerial(volumeSerial);
                if (!volumeRootPath.IsValid)
                {
                    return null;
                }

                if (
                    !FileUtilities.TryOpenDirectory(
                        volumeRootPath.Path,
                        FileShare.ReadWrite | FileShare.Delete,
                        out volumeRootHandle).Succeeded)
                {
                    Contract.Assert(volumeRootHandle == null);
                    return null;
                }

                m_volumeRootHandles.Add(volumeSerial, volumeRootHandle);
            }

            return volumeRootHandle;
        }
    }

    /// <summary>
    /// Allows opening a batch of volumes (devices) based on their volume serial numbers.
    /// </summary>
    /// <remarks>
    /// Unlike the <see cref="VolumeMap"/> upon which it operates, this class is not thread-safe.
    /// This class is disposable since it holds volume handles. At most, it holds one handle to each volume.
    /// Accessing volume handles is a privileged operation; attempting to open volume handles will likely fail if not elevated.
    /// </remarks>
    public sealed class VolumeAccessor : IDisposable
    {
        private Dictionary<ulong, SafeFileHandle> m_volumeHandles = new Dictionary<ulong, SafeFileHandle>();
        private readonly VolumeMap m_map;

        internal VolumeAccessor(VolumeMap map)
        {
            Contract.Requires(map != null);
            m_map = map;
            Disposed = false;
        }

        /// <summary>
        /// Creates a volume root handle or retrieves an existing one.
        /// </summary>
        /// <remarks>
        /// The returned handle should not be disposed.
        /// </remarks>
        public SafeFileHandle TryGetVolumeHandle(SafeFileHandle handleOnVolume)
        {
            return TryGetVolumeHandle(FileUtilities.GetVolumeSerialNumberByHandle(handleOnVolume));
        }

        /// <summary>
        /// Creates a volume root handle or retrieves an existing one.
        /// </summary>
        /// <remarks>
        /// This is the un-synchronized get-or-add operation resulting in <see cref="VolumeAccessor"/>
        /// not being thread-safe.
        /// The returned handle should not be disposed.
        /// </remarks>
        private SafeFileHandle TryGetVolumeHandle(ulong volumeSerial)
        {
            SafeFileHandle volumeHandle;
            if (!m_volumeHandles.TryGetValue(volumeSerial, out volumeHandle))
            {
                VolumeGuidPath volumeRootPath = m_map.TryGetVolumePathBySerial(volumeSerial);
                if (!volumeRootPath.IsValid)
                {
                    return null;
                }

                OpenFileResult openResult = FileUtilities.TryCreateOrOpenFile(
                    volumeRootPath.GetDevicePath(),
                    FileDesiredAccess.GenericRead,
                    FileShare.ReadWrite | FileShare.Delete,
                    FileMode.Open,
                    FileFlagsAndAttributes.None,
                    out volumeHandle);

                if (!openResult.Succeeded)
                {
                    Contract.Assert(volumeHandle == null);
                    return null;
                }

                m_volumeHandles.Add(volumeSerial, volumeHandle);
            }

            return volumeHandle;
        }

        /// <summary>
        /// Indicates if this instance has been disposed via <see cref="Dispose"/>.
        /// </summary>
        /// <remarks>
        /// Needs to be public for contract preconditions.
        /// </remarks>
        public bool Disposed { get; private set; }

        /// <summary>
        /// Closes any handles held by this instance.
        /// </summary>
        public void Dispose()
        {
            if (Disposed)
            {
                return;
            }

            foreach (SafeFileHandle handle in m_volumeHandles.Values)
            {
                handle.Dispose();
            }

            m_volumeHandles = null;

            Disposed = true;
        }
    }
}
