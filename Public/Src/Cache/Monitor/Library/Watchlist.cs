﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.Monitor.Library.Client;

namespace BuildXL.Cache.Monitor.App
{

    /// <summary>
    /// Stores dynamic properties regarding the stamps and environments under watch. These are loaded from Kusto on
    /// demand and can be refreshed at any time.
    /// </summary>
    internal sealed class Watchlist
    {
#pragma warning disable CS0649
        /// <remarks>
        /// WARNING: this class maps one to one to the result of the CacheMonitorWatchlist function. If changing,
        /// update both to ensure nothing breaks.
        /// </remarks>
        public record DynamicStampProperties : IEquatable<DynamicStampProperties>
        {
            public string Stamp = string.Empty;

            public string Ring = string.Empty;
        }
#pragma warning restore CS0649

        private IReadOnlyDictionary<StampId, DynamicStampProperties> _properties = new Dictionary<StampId, DynamicStampProperties>();

        public IEnumerable<StampId> Stamps => _properties.Keys;

        public IEnumerable<KeyValuePair<StampId, DynamicStampProperties>> Entries => _properties;

        public IReadOnlyDictionary<MonitorEnvironment, List<StampId>> EnvStamps =>
            _properties.GroupBy(property => property.Key.Environment, property => property.Key)
            .ToDictionary(group => group.Key, group => group.ToList());

        private readonly ILogger _logger;
        private readonly IReadOnlyDictionary<MonitorEnvironment, EnvironmentConfiguration> _environments;
        private readonly IReadOnlyDictionary<MonitorEnvironment, IKustoClient> _kustoClients;

        private Watchlist(ILogger logger, IReadOnlyDictionary<MonitorEnvironment, EnvironmentConfiguration> environments, IReadOnlyDictionary<MonitorEnvironment, IKustoClient> kustoClients)
        {
            _logger = logger;
            _environments = environments;
            _kustoClients = kustoClients;
        }

        /// <summary>
        /// Reloads the watchlist and settings for all stamps
        /// </summary>
        /// <returns>true iff the refresh caused a change in either the stamps being watched or the properties for any stamp</returns>
        public async Task<bool> RefreshAsync()
        {
            var newProperties = await LoadWatchlistAsync();

            // The properties are immutable, so this is mostly a formality to ensure that any threads that may be
            // concurrently accessing the watchlist do so in a well-defined way.
            var oldProperties = Interlocked.Exchange(ref _properties, newProperties);

            Func<KeyValuePair<StampId, DynamicStampProperties>, string> extract = kvp => kvp.Key.ToString();
            bool hasNotChanged = oldProperties.OrderBy(extract).SequenceEqual(newProperties.OrderBy(extract));

            return !hasNotChanged;
        }

        public static async Task<Watchlist> CreateAsync(ILogger logger, IReadOnlyDictionary<MonitorEnvironment, EnvironmentConfiguration> environments, IReadOnlyDictionary<MonitorEnvironment, IKustoClient> kustoClients)
        {
            var watchlist = new Watchlist(logger, environments, kustoClients);
            await watchlist.RefreshAsync();
            return watchlist;
        }

        private async Task<IReadOnlyDictionary<StampId, DynamicStampProperties>> LoadWatchlistAsync()
        {
            var query = @"CacheMonitorWatchlist";
            var watchlist = new Dictionary<StampId, DynamicStampProperties>();

            // This runs a set of blocking Kusto queries, which is pretty slow, so it's done concurrently
            await _environments.ParallelForEachAsync(async (keyValuePair) => {
                var envName = keyValuePair.Key;
                var envConf = keyValuePair.Value;
                var envKusto = _kustoClients[envName];

                _logger.Info("Loading monitor stamps for environment `{0}`", envName);

                var results = await envKusto.QueryAsync<DynamicStampProperties>(query, envConf.KustoDatabaseName);
                foreach (var result in results)
                {
                    Contract.AssertNotNullOrEmpty(result.Stamp);
                    Contract.AssertNotNullOrEmpty(result.Ring);

                    var entry = new StampId(envName, result.Stamp);
                    lock (watchlist) {
                        watchlist[entry] = result;
                    }

                    _logger.Debug("Monitoring stamp `{0}` on ring `{1}` in environment `{2}`", result.Stamp, result.Ring, envName);
                }
            });

            return watchlist;
        }

        public Result<DynamicStampProperties> TryGetProperties(StampId stampId)
        {
            if (_properties.TryGetValue(stampId, out var properties))
            {
                return properties;
            }

            return new Result<DynamicStampProperties>(errorMessage: $"Unknown stamp {stampId}");
        }
    }
}
