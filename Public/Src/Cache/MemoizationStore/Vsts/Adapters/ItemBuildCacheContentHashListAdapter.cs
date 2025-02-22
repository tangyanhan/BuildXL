// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Vsts;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.VstsInterfaces;
using Microsoft.VisualStudio.Services.WebApi;

namespace BuildXL.Cache.MemoizationStore.Vsts.Adapters
{
    /// <summary>
    /// An adapter to talk to the VSTS Build Cache Service with items.
    /// </summary>
    public class ItemBuildCacheContentHashListAdapter : IContentHashListAdapter
    {
        private static readonly ContentHashListWithCacheMetadata EmptyContentHashList = new ContentHashListWithCacheMetadata(
                   new ContentHashListWithDeterminism(null, CacheDeterminism.None),
                   null,
                   ContentAvailabilityGuarantee.NoContentBackedByCache);

        private readonly IBuildCacheHttpClient _buildCacheHttpClient;
        private readonly DownloadUriCache _downloadUriCache;

        /// <summary>
        /// Initializes a new instance of the <see cref="ItemBuildCacheContentHashListAdapter"/> class.
        /// </summary>
        public ItemBuildCacheContentHashListAdapter(IBuildCacheHttpClient buildCacheHttpClient, DownloadUriCache downloadUriCache)
        {
            _buildCacheHttpClient = buildCacheHttpClient;
            _downloadUriCache = downloadUriCache;
        }

        /// <inheritdoc />
        public async Task<Result<IEnumerable<SelectorAndContentHashListWithCacheMetadata>>> GetSelectorsAsync(
            Context context,
            string cacheNamespace,
            Fingerprint weakFingerprint,
            int maxSelectorsToFetch)
        {
            try
            {
                var selectorsResponse = await ArtifactHttpClientErrorDetectionStrategy.ExecuteWithTimeoutAsync(
                    context,
                    "GetSelectors",
                    innerCts => _buildCacheHttpClient.GetSelectors(
                        cacheNamespace,
                        weakFingerprint,
                        maxSelectorsToFetch), CancellationToken.None);
                var selectorsToReturn = new List<SelectorAndContentHashListWithCacheMetadata>();
                foreach (
                    SelectorAndPossibleContentHashListResponse selectorAndPossible in selectorsResponse.SelectorsAndPossibleContentHashLists
                )
                {
                    if (selectorAndPossible.ContentHashList != null)
                    {
                        _downloadUriCache.BulkAddDownloadUris(selectorAndPossible.ContentHashList.BlobDownloadUris);
                    }

                    selectorsToReturn.Add(
                        new SelectorAndContentHashListWithCacheMetadata(
                            selectorAndPossible.Selector,
                            selectorAndPossible.ContentHashList?.ContentHashListWithCacheMetadata));
                }

                return new Result<IEnumerable<SelectorAndContentHashListWithCacheMetadata>>(selectorsToReturn);
            }
            catch (Exception ex)
            {
                return new Result<IEnumerable<SelectorAndContentHashListWithCacheMetadata>>(ex);
            }
        }

        /// <inheritdoc />
        public async Task<Result<ContentHashListWithCacheMetadata>> GetContentHashListAsync(
            Context context,
            string cacheNamespace,
            StrongFingerprint strongFingerprint)
        {
            try
            {
                ContentHashListResponse response =
                    await ArtifactHttpClientErrorDetectionStrategy.ExecuteWithTimeoutAsync(
                        context,
                        "GetContentHashList",
                        innerCts => _buildCacheHttpClient.GetContentHashListAsync(cacheNamespace, strongFingerprint),
                        CancellationToken.None).ConfigureAwait(false);

                _downloadUriCache.BulkAddDownloadUris(response.BlobDownloadUris);

                // our response should never be null.
                if (response.ContentHashListWithCacheMetadata != null)
                {
                    return new Result<ContentHashListWithCacheMetadata>(response.ContentHashListWithCacheMetadata);
                }

                return new Result<ContentHashListWithCacheMetadata>(EmptyContentHashList);
            }
            catch (CacheServiceException ex) when (ex.ReasonCode == CacheErrorReasonCode.ContentHashListNotFound)
            {
                return new Result<ContentHashListWithCacheMetadata>(EmptyContentHashList);
            }
            catch (ContentBagNotFoundException)
            {
                return new Result<ContentHashListWithCacheMetadata>(EmptyContentHashList);
            }
            catch (VssServiceResponseException serviceEx) when (serviceEx.HttpStatusCode == HttpStatusCode.NotFound)
            {
                // Currently expect the Item-based service to return VssServiceResponseException on misses,
                // but the other catches have been left for safety/compat.
                return new Result<ContentHashListWithCacheMetadata>(EmptyContentHashList);
            }
            catch (Exception ex)
            {
                return new Result<ContentHashListWithCacheMetadata>(ex);
            }
        }

        /// <inheritdoc />
        public async Task<Result<ContentHashListWithCacheMetadata>> AddContentHashListAsync(
            Context context,
            string cacheNamespace,
            StrongFingerprint strongFingerprint,
            ContentHashListWithCacheMetadata valueToAdd,
            bool forceUpdate)
        {
            try
            {
                ContentHashListResponse addResult = await ArtifactHttpClientErrorDetectionStrategy.ExecuteWithTimeoutAsync(
                    context,
                    "AddContentHashList",
                    innerCts => _buildCacheHttpClient.AddContentHashListAsync(
                        cacheNamespace,
                        strongFingerprint,
                        valueToAdd,
                        forceUpdate), CancellationToken.None).ConfigureAwait(false);

                // The return value is null if the server fails adding content hash list to the backing store.
                // See BuildCacheService.AddContentHashListAsync for more details about the implementation invariants/guarantees.
                if (addResult != null)
                {
                    _downloadUriCache.BulkAddDownloadUris(addResult.BlobDownloadUris);
                }

                // add succeeded but returned an empty contenthashlistwith cache metadata. correct this.
                if (addResult?.ContentHashListWithCacheMetadata == null)
                {
                    return
                        new Result<ContentHashListWithCacheMetadata>(
                            new ContentHashListWithCacheMetadata(
                                new ContentHashListWithDeterminism(null, valueToAdd.ContentHashListWithDeterminism.Determinism),
                                valueToAdd.GetRawExpirationTimeUtc(),
                                valueToAdd.ContentGuarantee,
                                valueToAdd.HashOfExistingContentHashList));
                }
                else if (addResult.ContentHashListWithCacheMetadata.ContentHashListWithDeterminism.ContentHashList != null
                         && addResult.ContentHashListWithCacheMetadata.HashOfExistingContentHashList == null)
                {
                    return new Result<ContentHashListWithCacheMetadata>(
                        new ContentHashListWithCacheMetadata(
                            addResult.ContentHashListWithCacheMetadata.ContentHashListWithDeterminism,
                            addResult.ContentHashListWithCacheMetadata.GetRawExpirationTimeUtc(),
                            addResult.ContentHashListWithCacheMetadata.ContentGuarantee,
                            addResult.ContentHashListWithCacheMetadata.ContentHashListWithDeterminism.ContentHashList.GetHashOfHashes()));
                }
                else
                {
                    return new Result<ContentHashListWithCacheMetadata>(addResult.ContentHashListWithCacheMetadata);
                }
            }
            catch (Exception ex)
            {
                return new Result<ContentHashListWithCacheMetadata>(ex);
            }
        }

        /// <inheritdoc />
        public Task IncorporateStrongFingerprints(
            Context context,
            string cacheNamespace,
            IncorporateStrongFingerprintsRequest incorporateStrongFingerprintsRequest)
        {
            return ArtifactHttpClientErrorDetectionStrategy.ExecuteAsync(
                context,
                "IncorporateStrongFingerprints",
                () => _buildCacheHttpClient.IncorporateStrongFingerprints(cacheNamespace, incorporateStrongFingerprintsRequest),
                CancellationToken.None);
        }
    }
}
