// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using BuildXL.Native.IO;
using BuildXL.Native.IO.Unix;
using BuildXL.Utilities;
using Microsoft.Win32.SafeHandles;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Storage
{
    /// <summary>
    /// Tests for creating, opening, and managing files.
    /// </summary>
    public sealed class NativeFileManagementTests : TemporaryStorageTestBase
    {
        // TODO - need to investigate equivalent on Unix
        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void DeleteDispositionResultsInDeletion()
        {
            string path = Path.Combine(TemporaryDirectory, "toDelete");
            File.WriteAllText(path, "Important data");

            SafeFileHandle handle;
            var openResult = FileUtilities.TryCreateOrOpenFile(
                path,
                FileDesiredAccess.Delete,
                FileShare.None,
                FileMode.Open,
                FileFlagsAndAttributes.None,
                out handle);
            using (handle)
            {
                XAssert.IsNotNull(handle);
                XAssert.IsTrue(openResult.Succeeded);
                XAssert.IsTrue(openResult.OpenedOrTruncatedExistingFile);
                XAssert.IsTrue(FileUtilities.TrySetDeletionDisposition(handle), "Failed to set deletion disposition");
            }

            XAssert.IsFalse(File.Exists(path), "File not unlinked after handle close.");
        }

        // TryReOpenFile not supported on Unix
        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void ReOpenFileSuccess()
        {
            string path = Path.Combine(TemporaryDirectory, "file");

            using (var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite))
            {
                SafeFileHandle writableHandle;
                ReOpenFileStatus status = FileUtilities.TryReOpenFile(
                    stream.SafeFileHandle,
                    FileDesiredAccess.GenericWrite,
                    FileShare.ReadWrite,
                    FileFlagsAndAttributes.None,
                    out writableHandle);

                using (writableHandle)
                {
                    XAssert.AreEqual(ReOpenFileStatus.Success, status);
                    XAssert.IsNotNull(writableHandle);
                    XAssert.IsFalse(writableHandle.IsInvalid);

                    using (var writableStream = new FileStream(writableHandle, FileAccess.Write, bufferSize: 128, isAsync: false))
                    {
                        writableStream.WriteByte(0xab);
                    }
                }

                int read = stream.ReadByte();
                XAssert.AreEqual(0xab, read);
            }
        }

        // TryReOpenFile not supported on Unix
        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void ReOpenFileSharingViolation()
        {
            string path = Path.Combine(TemporaryDirectory, "file");

            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                SafeFileHandle writableHandle;
                ReOpenFileStatus status = FileUtilities.TryReOpenFile(
                    stream.SafeFileHandle,
                    FileDesiredAccess.GenericWrite,
                    FileShare.ReadWrite,
                    FileFlagsAndAttributes.None,
                    out writableHandle);

                using (writableHandle)
                {
                    XAssert.AreEqual(ReOpenFileStatus.SharingViolation, status);
                    XAssert.IsNull(writableHandle);
                }
            }
        }

        [Fact(Skip = "Run this when it is possible to reliably determine win10 rs2 version.")]
        public void CanPosixDelete()
        {
            string path = Path.Combine(TemporaryDirectory, "file");
            File.WriteAllText(path, "Important data");

            bool ret = FileUtilities.TryPosixDelete(path, out var result);

            XAssert.AreEqual(true, ret);
        }

        [Fact]
        public void RemoveDirectoryWithException()
        {
            string rootDir = GetFullPath("Directory");
            Directory.CreateDirectory(rootDir);
            Directory.CreateDirectory(Path.Combine(rootDir, "subdir1"));
            Assert.Throws<NativeWin32Exception>(() => FileUtilities.RemoveDirectory(rootDir));
        }

        [Fact]
        public void TryRemoveDirectory()
        {
            string rootDir = GetFullPath("Directory");
            Directory.CreateDirectory(rootDir);
            Directory.CreateDirectory(Path.Combine(rootDir, "subdir1"));

            int hresult;
            XAssert.IsFalse(FileUtilities.TryRemoveDirectory(rootDir, out hresult));
            XAssert.AreNotEqual(0, hresult);
        }

        [Fact]
        public void EnumerateEmptyDirectoryNames()
        {
            string path = Path.Combine(TemporaryDirectory, "emptyDir");
            Directory.CreateDirectory(path);

            var names = new List<string>();
            EnumerateDirectoryResult result = FileUtilities.EnumerateDirectoryEntries(path, (name, attr) => names.Add(name));
            XAssert.IsTrue(result.Succeeded);
            XAssert.AreEqual(EnumerateDirectoryStatus.Success, result.Status);

            XAssert.AreEqual(0, names.Count);
        }

        [Fact]
        public void EnumerateNonEmptyDirectory()
        {
            string path = Path.Combine(TemporaryDirectory, "dir");
            Directory.CreateDirectory(path);

            WriteFile(PathGeneratorUtilities.GetRelativePath("dir","fileA"), string.Empty);
            Directory.CreateDirectory(GetFullPath(PathGeneratorUtilities.GetRelativePath("dir","dirB")));

            var names = new List<string>();
            EnumerateDirectoryResult result = FileUtilities.EnumerateDirectoryEntries(path, (name, attr) => names.Add(name));
            XAssert.IsTrue(result.Succeeded);
            XAssert.AreEqual(EnumerateDirectoryStatus.Success, result.Status);

            Assert.Contains("fileA", names, StringComparer.Ordinal);
            Assert.Contains("dirB", names, StringComparer.Ordinal);
            Assert.Equal(2, names.Count);
        }

        [Fact]
        public void EnumerationIndicatesDirectories()
        {
            string path = Path.Combine(TemporaryDirectory, "dir");
            Directory.CreateDirectory(path);

            Directory.CreateDirectory(GetFullPath(R("dir","childDir")));
            Directory.CreateDirectory(GetFullPath(R("dir","childDir2")));

            var names = new List<string>();
            EnumerateDirectoryResult result = FileUtilities.EnumerateDirectoryEntries(
                path,
                (name, attr) =>
                {
                    XAssert.IsTrue((attr & FileAttributes.Directory) != 0, "All children are directories.");
                    names.Add(name);
                });

            XAssert.IsTrue(result.Succeeded);
            XAssert.AreEqual(EnumerateDirectoryStatus.Success, result.Status);
            XAssert.SetEqual(new[] { "childDir", "childDir2" }, names);
        }

        [Fact]
        public void EnumerationIndicatesFiles()
        {
            string path = Path.Combine(TemporaryDirectory, "dir");
            Directory.CreateDirectory(path);

            WriteFile(R("dir", "fileA"), string.Empty);
            WriteFile(R("dir", "fileB"), string.Empty);

            var names = new List<string>();
            EnumerateDirectoryResult result = FileUtilities.EnumerateDirectoryEntries(
                path,
                (name, attr) =>
                {
                    XAssert.IsTrue((attr & FileAttributes.Directory) == 0, "All children are files.");
                    names.Add(name);
                });
            XAssert.IsTrue(result.Succeeded);
            XAssert.AreEqual(EnumerateDirectoryStatus.Success, result.Status);
            XAssert.SetEqual(new[] { "fileA", "fileB" }, names);
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public void EnumerationSkipsDSStoreFiles()
        {
            string path = Path.Combine(TemporaryDirectory, "dir");
            Directory.CreateDirectory(path);

            WriteFile(PathGeneratorUtilities.GetRelativePath("dir", FileSystemUnix.DsStoreMetaFileName), string.Empty);
            WriteFile(PathGeneratorUtilities.GetRelativePath("dir", "someFile"), string.Empty);

            var names = new List<string>();

            // Plain enumeration should NOT include .DS_Store meta files
            var result = FileUtilities.EnumerateDirectoryEntries(
                path,
                (name, attr) =>
                {
                    XAssert.IsTrue((attr & FileAttributes.Directory) == 0, "All children are files.");
                    names.Add(name);
                });

            XAssert.IsTrue(result.Succeeded);
            XAssert.AreEqual(EnumerateDirectoryStatus.Success, result.Status);
            XAssert.SetEqual(new[] { "someFile" }, names);
        }

        [FactIfSupported(requiresUnixBasedOperatingSystem: true)]
        public void RemoveDirectoryWithDSStoreFileSucceeds()
        {
            string path = Path.Combine(TemporaryDirectory, "dir");
            Directory.CreateDirectory(path);

            WriteFile(PathGeneratorUtilities.GetRelativePath("dir", FileSystemUnix.DsStoreMetaFileName), string.Empty);
            WriteFile(PathGeneratorUtilities.GetRelativePath("dir", "someFile"), string.Empty);

            try
            {
                FileUtilities.DeleteDirectoryContents(path, deleteRootDirectory: true);
            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch
            {
                // Deleting a directory should enumerate the .DS_Store files and delete them too
                Assert.True(false);
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
        }

        [Fact]
        public void FailEnumerateFile()
        {
            string path = GetFullPath(@"NotADirectory");
            WriteFile(@"NotADirectory", string.Empty);

            var names = new List<string>();
            EnumerateDirectoryResult result = FileUtilities.EnumerateDirectoryEntries(path, (name, attr) => names.Add(name));
            XAssert.IsFalse(result.Succeeded);
            XAssert.AreEqual(EnumerateDirectoryStatus.CannotEnumerateFile, result.Status);

            XAssert.AreEqual(0, names.Count);
        }

        [Fact]
        public void FailEnumerateAbsentDirectory()
        {
            string path = GetFullPath(@"DoesNotExist");

            var names = new List<string>();
            EnumerateDirectoryResult result = FileUtilities.EnumerateDirectoryEntries(path, (name, attr) => names.Add(name));
            XAssert.IsFalse(result.Succeeded);
            XAssert.AreEqual(EnumerateDirectoryStatus.SearchDirectoryNotFound, result.Status);

            XAssert.AreEqual(0, names.Count);
        }
    }
}
