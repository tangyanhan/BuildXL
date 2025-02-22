// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;

namespace BuildXL.Pips
{
    /// <summary>
    /// Mutable pip state
    /// </summary>
    /// <remarks>
    /// While a <code>Pip</code> is strictly immutable, all mutable information associated with a Pip goes here.
    /// (This class also holds some immutable information that is often needed to identify a Pip,
    /// in particular <code>NodeIdValue</code>, <code>SemiStableHash</code>.)
    /// </remarks>
    internal class MutablePipState
    {
        /// <summary>
        /// Identifier of this pip that is stable across BuildXL runs with an identical schedule
        /// </summary>
        /// <remarks>
        /// This identifier is not necessarily unique, but should be quite unique in practice.
        /// This property is equivalent to <see cref="Pip.SemiStableHash"/> for the pip represented, and therefore immutable.
        /// </remarks>
        internal long SemiStableHash { get; }

        /// <summary>
        /// Associated PageableStoreId for the mutable. Used to retrieve the corresponding Pip
        /// </summary>
        internal PageableStoreId StoreId;

        private WeakReference<Pip> m_weakPip;

        internal readonly PipType PipType;
        /// <summary>
        /// /// Constructor used for deserialization
        /// </summary>
        protected MutablePipState(PipType piptype, long semiStableHash, PageableStoreId storeId)
        {
            PipType = piptype;
            SemiStableHash = semiStableHash;
            StoreId = storeId;
        }

        /// <summary>
        /// Creates a new mutable from a live pip
        /// </summary>
        public static MutablePipState Create(Pip pip)
        {
            Contract.Requires(pip != null);
            Contract.Assert(PipState.Ignored == 0);

            MutablePipState mutable;

            switch (pip.PipType)
            {
                case PipType.Ipc:
                    var pipAsIpc = (IpcPip)pip;
                    var serviceKind =
                        pipAsIpc.IsServiceFinalization ? ServicePipKind.ServiceFinalization :
                        pipAsIpc.ServicePipDependencies.Any() ? ServicePipKind.ServiceClient :
                        ServicePipKind.None;
                    var serviceInfo = new ServiceInfo(serviceKind, pipAsIpc.ServicePipDependencies);
                    mutable = new ProcessMutablePipState(pip.PipType, pip.SemiStableHash, default(PageableStoreId), serviceInfo, Process.Options.IsLight, default(RewritePolicy), AbsolutePath.Invalid, Process.MinPriority, pip.Provenance.ModuleId);
                    break;
                case PipType.Process:
                    var pipAsProcess = (Process)pip;
                    mutable = new ProcessMutablePipState(
                        pip.PipType, 
                        pip.SemiStableHash, 
                        default(PageableStoreId), 
                        pipAsProcess.ServiceInfo, 
                        pipAsProcess.ProcessOptions, 
                        pipAsProcess.RewritePolicy, 
                        pipAsProcess.Executable.Path, 
                        pipAsProcess.Priority,
                        pipAsProcess.Provenance.ModuleId,
                        preserveOutputsTrustLevel: pipAsProcess.PreserveOutputsTrustLevel,
                        isSucceedFast: pipAsProcess.SucceedFastExitCodes.Length > 0);
                    break;
                case PipType.CopyFile:
                    var pipAsCopy = (CopyFile)pip;
                    mutable = new CopyMutablePipState(pip.PipType, pip.SemiStableHash, default(PageableStoreId), pipAsCopy.OutputsMustRemainWritable);
                    break;
                case PipType.SealDirectory:
                    var seal = (SealDirectory)pip;
                    
                    mutable = new SealDirectoryMutablePipState(
                        pip.PipType,
                        pip.SemiStableHash,
                        default(PageableStoreId),
                        seal.DirectoryRoot,
                        seal.Kind,
                        seal.Patterns,
                        seal.IsComposite,
                        seal.Scrub,
                        seal.ContentFilter);
                    break;
                default:
                    mutable = new MutablePipState(pip.PipType, pip.SemiStableHash, default(PageableStoreId));
                    break;
            }

            mutable.m_weakPip = new WeakReference<Pip>(pip);
            return mutable;
        }

        /// <summary>
        /// Serializes
        /// </summary>
        internal void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);

            writer.Write((byte)PipType);
            writer.Write(SemiStableHash);
            StoreId.Serialize(writer);
            SpecializedSerialize(writer);
        }

        /// <summary>
        /// Deserializes
        /// </summary>
        internal static MutablePipState Deserialize(BuildXLReader reader)
        {
            Contract.Requires(reader != null);

            var pipType = (PipType)reader.ReadByte();
            var semiStableHash = reader.ReadInt64();
            var storeId = PageableStoreId.Deserialize(reader);

            switch (pipType)
            {
                case PipType.Ipc:
                case PipType.Process:
                    return ProcessMutablePipState.Deserialize(reader, pipType, semiStableHash, storeId);
                case PipType.CopyFile:
                    return CopyMutablePipState.Deserialize(reader, pipType, semiStableHash, storeId);
                case PipType.SealDirectory:
                    return SealDirectoryMutablePipState.Deserialize(reader, pipType, semiStableHash, storeId);
                default:
                    return new MutablePipState(pipType, semiStableHash, storeId);
            }
        }

        /// <summary>
        /// Implemented by derived classes that need custom deserialization
        /// </summary>
        protected virtual void SpecializedSerialize(BuildXLWriter writer)
        {
        }

        /// <summary>
        /// Checks if pip outputs must remain writable.
        /// </summary>
        /// <returns></returns>
        public virtual bool MustOutputsRemainWritable() => false;

        /// <summary>
        /// Checks if pip outputs must be preserved.
        /// </summary>
        /// <returns></returns>
        public virtual bool IsPreservedOutputsPip() => false;

        /// <summary>
        /// Checks if pip runs tool with incremental capability.
        /// </summary>
        /// <returns></returns>
        public virtual bool IsIncrementalTool() => false;

        /// <summary>
        /// Checks if pip using a non-empty preserveOutputAllowlist
        /// </summary>
        /// <returns></returns>
        public virtual bool HasPreserveOutputAllowlist() => false;

        /// <summary>
        /// Get pip preserve outputs trust level
        /// </summary>
        /// <returns></returns>
        public virtual int GetProcessPreserveOutputsTrustLevel() => 0;

        internal bool IsAlive
        {
            get
            {
                if (m_weakPip == null)
                {
                    return false;
                }

                Pip pip;
                return m_weakPip.TryGetTarget(out pip);
            }
        }

        internal Pip InternalGetOrSetPip(PipTable table, PipId pipId, PipQueryContext context, Func<PipTable, PipId, PageableStoreId, PipQueryContext, Pip> creator)
        {
            lock (this)
            {
                Pip pip;
                if (m_weakPip == null)
                {
                    pip = creator(table, pipId, StoreId, context);
                    m_weakPip = new WeakReference<Pip>(pip);
                }
                else if (!m_weakPip.TryGetTarget(out pip))
                {
                    m_weakPip.SetTarget(pip = creator(table, pipId, StoreId, context));
                }

                return pip;
            }
        }
    }

    /// <summary>
    /// Mutable pip state for Process pips.
    /// </summary>
    internal sealed class ProcessMutablePipState : MutablePipState
    {
        internal readonly ServiceInfo ServiceInfo;
        internal readonly Process.Options ProcessOptions;
        internal readonly int Priority;
        internal readonly int PreserveOutputTrustLevel;
        internal readonly RewritePolicy RewritePolicy;
        internal readonly AbsolutePath ExecutablePath;
        internal readonly ModuleId ModuleId;
        internal readonly bool IsSucceedFast;

        internal ProcessMutablePipState(
            PipType pipType,
            long semiStableHash,
            PageableStoreId storeId,
            ServiceInfo serviceInfo,
            Process.Options processOptions,
            RewritePolicy rewritePolicy,
            AbsolutePath executablePath,
            int priority,
            ModuleId moduleId,
            int preserveOutputsTrustLevel = 0,
            bool isSucceedFast = false)
            : base(pipType, semiStableHash, storeId)
        {
            ServiceInfo = serviceInfo;
            ProcessOptions = processOptions;
            RewritePolicy = rewritePolicy;
            ExecutablePath = executablePath;
            Priority = priority;
            PreserveOutputTrustLevel = preserveOutputsTrustLevel;
            ModuleId = moduleId;
            IsSucceedFast = isSucceedFast;
        }

        /// <summary>
        /// Shortcut for whether this is a start or shutdown pip
        /// </summary>
        internal bool IsStartOrShutdown
        {
            get
            {
                return ServiceInfo != null && ServiceInfo.Kind.IsStartOrShutdown();
            }
        }

        protected override void SpecializedSerialize(BuildXLWriter writer)
        {
            writer.Write(ServiceInfo, ServiceInfo.InternalSerialize);
            writer.Write((int)ProcessOptions);
            writer.Write((byte)RewritePolicy);
            writer.Write(ExecutablePath);
            writer.Write(Priority);
            writer.Write(PreserveOutputTrustLevel);
            writer.Write(ModuleId);
            writer.Write(IsSucceedFast);
        }

        internal static MutablePipState Deserialize(BuildXLReader reader, PipType pipType, long semiStableHash, PageableStoreId storeId)
        {
            ServiceInfo serviceInfo = reader.ReadNullable(ServiceInfo.InternalDeserialize);
            int options = reader.ReadInt32();
            RewritePolicy rewritePolicy = (RewritePolicy) reader.ReadByte();
            AbsolutePath executablePath = reader.ReadAbsolutePath();
            int priority = reader.ReadInt32();
            int preserveOutputTrustLevel = reader.ReadInt32();
            ModuleId moduleId = reader.ReadModuleId();
            bool isSucceedFast = reader.ReadBoolean();

            return new ProcessMutablePipState(
                pipType,
                semiStableHash,
                storeId,
                serviceInfo,
                (Process.Options)options,
                rewritePolicy,
                executablePath,
                priority,
                moduleId,
                preserveOutputsTrustLevel: preserveOutputTrustLevel,
                isSucceedFast: isSucceedFast);
        }

        public override bool IsPreservedOutputsPip() => (ProcessOptions & Process.Options.AllowPreserveOutputs) != 0;

        public override bool IsIncrementalTool() => (ProcessOptions & Process.Options.IncrementalTool) == Process.Options.IncrementalTool;

        public override bool HasPreserveOutputAllowlist() => (ProcessOptions & Process.Options.HasPreserveOutputAllowlist) != 0;

        public override bool MustOutputsRemainWritable() => (ProcessOptions & Process.Options.OutputsMustRemainWritable) != 0;

        public override int GetProcessPreserveOutputsTrustLevel() => PreserveOutputTrustLevel;
    }

    internal sealed class CopyMutablePipState : MutablePipState
    {
        private readonly bool m_keepOutputWritable;

        internal CopyMutablePipState(
            PipType pipType,
            long semiStableHash,
            PageableStoreId storeId,
            bool keepOutputWritable)
            : base(pipType, semiStableHash, storeId)
        {
            m_keepOutputWritable = keepOutputWritable;
        }

        protected override void SpecializedSerialize(BuildXLWriter writer)
        {
            writer.Write(m_keepOutputWritable);
        }

        internal static MutablePipState Deserialize(BuildXLReader reader, PipType pipType, long semiStableHash, PageableStoreId storeId)
        {
            bool keepOutputWritable = reader.ReadBoolean();

            return new CopyMutablePipState(pipType, semiStableHash, storeId, keepOutputWritable);
        }

        public override bool MustOutputsRemainWritable() => m_keepOutputWritable;
    }

    /// <summary>
    /// Mutable pip state for SealDirectory pips.
    /// </summary>
    internal sealed class SealDirectoryMutablePipState : MutablePipState
    {
        internal readonly AbsolutePath DirectoryRoot;
        internal readonly SealDirectoryKind SealDirectoryKind;
        internal readonly ReadOnlyArray<StringId> Patterns;
        internal readonly bool IsComposite;
        internal readonly bool Scrub;
        internal readonly SealDirectoryContentFilter? ContentFilter;

        public SealDirectoryMutablePipState(
            PipType piptype,
            long semiStableHash,
            PageableStoreId storeId,
            AbsolutePath directoryRoot,
            SealDirectoryKind sealDirectoryKind,
            ReadOnlyArray<StringId> patterns,
            bool isComposite,
            bool scrub,
            SealDirectoryContentFilter? contentFilter)
            : base(piptype, semiStableHash, storeId)
        {
            DirectoryRoot = directoryRoot;
            SealDirectoryKind = sealDirectoryKind;
            Patterns = patterns;
            IsComposite = isComposite;
            Scrub = scrub;
            ContentFilter = contentFilter;
        }

        protected override void SpecializedSerialize(BuildXLWriter writer)
        {
            writer.Write(DirectoryRoot);
            writer.Write((byte)SealDirectoryKind);
            writer.Write(Patterns, (w, v) => w.Write(v));
            writer.Write(IsComposite);
            writer.Write(Scrub);
            if (ContentFilter != null)
            {
                writer.Write(true);
                writer.Write((byte)ContentFilter.Value.Kind);
                writer.Write(ContentFilter.Value.Regex);
            }
            else
            {
                writer.Write(false);
            }

        }

        internal static MutablePipState Deserialize(BuildXLReader reader, PipType pipType, long semiStableHash, PageableStoreId storeId)
        {
            var directoryRoot = reader.ReadAbsolutePath();
            var sealDirectoryKind = (SealDirectoryKind)reader.ReadByte();
            var patterns = reader.ReadReadOnlyArray(reader1 => reader1.ReadStringId());
            var isComposite = reader.ReadBoolean();
            var scrub = reader.ReadBoolean();
            SealDirectoryContentFilter? contentFilter = null;
            if (reader.ReadBoolean())
            {
                contentFilter = new SealDirectoryContentFilter((SealDirectoryContentFilter.ContentFilterKind)reader.ReadByte(), reader.ReadString());
            }

            return new SealDirectoryMutablePipState(
                pipType,
                semiStableHash,
                storeId,
                directoryRoot,
                sealDirectoryKind,
                patterns,
                isComposite,
                scrub,
                contentFilter);
        }
    }
}
