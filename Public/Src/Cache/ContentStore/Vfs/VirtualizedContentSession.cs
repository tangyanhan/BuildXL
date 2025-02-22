// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Vfs
{
    /// <todoc />
    internal class VirtualizedContentSession : ContentSessionBase
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(VirtualizedContentSession));

        // Should not trace since it mostly delegates to underlying content session
        protected override bool TraceErrorsOnly => true;

        private readonly IContentSession _innerSession;
        private readonly VirtualizedContentStore _store;
        private readonly PassThroughFileSystem _fileSystem;
        private readonly VfsContentManager _contentManager;

        public VirtualizedContentSession(VirtualizedContentStore store, IContentSession session, VfsContentManager contentManager, string name)
            : base(name)
        {
            _store = store;
            _innerSession = session;
            _contentManager = contentManager;
            _fileSystem = new PassThroughFileSystem();
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            await _innerSession.StartupAsync(context).ThrowIfFailure();
            return await base.StartupCoreAsync(context);
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            var result = await base.ShutdownCoreAsync(context);
            result &= await _innerSession.ShutdownAsync(context);
            return result;
        }

        /// <inheritdoc />
        protected override Task<OpenStreamResult> OpenStreamCoreAsync(OperationContext operationContext, ContentHash contentHash, UrgencyHint urgencyHint, Counter retryCounter)
        {
            return _innerSession.OpenStreamAsync(operationContext, contentHash, operationContext.Token, urgencyHint);
        }

        /// <inheritdoc />
        protected override Task<PinResult> PinCoreAsync(OperationContext operationContext, ContentHash contentHash, UrgencyHint urgencyHint, Counter retryCounter)
        {
            return _innerSession.PinAsync(operationContext, contentHash, operationContext.Token, urgencyHint);
        }

        /// <inheritdoc />
        protected override Task<IEnumerable<Task<Indexed<PinResult>>>> PinCoreAsync(OperationContext operationContext, IReadOnlyList<ContentHash> contentHashes, UrgencyHint urgencyHint, Counter retryCounter, Counter fileCounter)
        {
            return _innerSession.PinAsync(operationContext, contentHashes, operationContext.Token, urgencyHint);
        }

        protected override bool TraceErrorsOnlyForPlaceFile(AbsolutePath path)
        {
            return !path.IsVirtual;
        }

        /// <inheritdoc />
        protected override async Task<PlaceFileResult> PlaceFileCoreAsync(OperationContext operationContext, ContentHash contentHash, AbsolutePath path, FileAccessMode accessMode, FileReplacementMode replacementMode, FileRealizationMode realizationMode, UrgencyHint urgencyHint, Counter retryCounter)
        {
            if (!path.IsVirtual)
            {
                return await _innerSession.PlaceFileAsync(
                    operationContext,
                    contentHash,
                    path,
                    accessMode,
                    replacementMode,
                    realizationMode,
                    operationContext.Token,
                    urgencyHint);
            }

            if (replacementMode != FileReplacementMode.ReplaceExisting && _fileSystem.FileExists(path))
            {
                if (replacementMode == FileReplacementMode.SkipIfExists)
                {
                    return PlaceFileResult.AlreadyExists;
                }
                else if (replacementMode == FileReplacementMode.FailIfExists)
                {
                    return new PlaceFileResult(
                        PlaceFileResult.ResultCode.Error,
                        $"File exists at destination {path} with FailIfExists specified");
                }
            }

            var placementData = new VfsFilePlacementData(contentHash, realizationMode, accessMode);

            return await _contentManager.PlaceFileAsync(operationContext, path, placementData, operationContext.Token);
        }

        /// <inheritdoc />
        protected override Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileCoreAsync(OperationContext operationContext, IReadOnlyList<ContentHashWithPath> hashesWithPaths, FileAccessMode accessMode, FileReplacementMode replacementMode, FileRealizationMode realizationMode, UrgencyHint urgencyHint, Counter retryCounter)
        {
            // NOTE: Most of the IContentSession implementations throw NotImplementedException, most notably
            // the ReadOnlyServiceClientContentSession which is used to communicate with this session. Given that,
            // it is safe for this method to not be implemented here as well.
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override Task<PutResult> PutFileCoreAsync(OperationContext operationContext, ContentHash contentHash, AbsolutePath path, FileRealizationMode realizationMode, UrgencyHint urgencyHint, Counter retryCounter)
        {
            return _innerSession.PutFileAsync(operationContext, contentHash, path, realizationMode, operationContext.Token, urgencyHint);
        }

        /// <inheritdoc />
        protected override Task<PutResult> PutFileCoreAsync(OperationContext operationContext, HashType hashType, AbsolutePath path, FileRealizationMode realizationMode, UrgencyHint urgencyHint, Counter retryCounter)
        {
            return _innerSession.PutFileAsync(operationContext, hashType, path, realizationMode, operationContext.Token, urgencyHint);
        }

        /// <inheritdoc />
        protected override Task<PutResult> PutStreamCoreAsync(OperationContext operationContext, ContentHash contentHash, Stream stream, UrgencyHint urgencyHint, Counter retryCounter)
        {
            return _innerSession.PutStreamAsync(operationContext, contentHash, stream, operationContext.Token, urgencyHint);
        }

        /// <inheritdoc />
        protected override Task<PutResult> PutStreamCoreAsync(OperationContext operationContext, HashType hashType, Stream stream, UrgencyHint urgencyHint, Counter retryCounter)
        {
            return _innerSession.PutStreamAsync(operationContext, hashType, stream, operationContext.Token, urgencyHint);
        }
    }
}
