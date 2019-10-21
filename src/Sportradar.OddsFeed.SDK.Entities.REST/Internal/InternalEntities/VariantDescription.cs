﻿/*
* Copyright (C) Sportradar AG. See LICENSE for full license governing this code
*/
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Dawn;
using System.Globalization;
using System.Linq;
using Sportradar.OddsFeed.SDK.Entities.REST.Internal.MarketNames;
using Sportradar.OddsFeed.SDK.Entities.REST.Market;
using Sportradar.OddsFeed.SDK.Entities.REST.MarketMapping;

namespace Sportradar.OddsFeed.SDK.Entities.REST.Internal.InternalEntities
{
    internal class VariantDescription : IVariantDescription
    {
        public string Id { get; }

        public IEnumerable<IOutcomeDescription> Outcomes { get; }

        public IEnumerable<IMarketMappingData> Mappings { get; internal set; }

        internal VariantDescriptionCacheItem VariantDescriptionCacheItem { get; }

        internal VariantDescription(VariantDescriptionCacheItem cacheItem, IEnumerable<CultureInfo> cultures)
        {
            Guard.Argument(cacheItem).NotNull();
            var cultureInfos = cultures.ToList();
            Guard.Argument(cultureInfos).NotNull().NotEmpty();

            Id = cacheItem.Id;
            Outcomes = cacheItem.Outcomes == null
                ? null
                : new ReadOnlyCollection<IOutcomeDescription>(cacheItem.Outcomes.Select(o => (IOutcomeDescription) new OutcomeDescription(o, cultureInfos)).ToList());
            Mappings = cacheItem.Mappings == null
                ? null
                : new ReadOnlyCollection<IMarketMappingData>(cacheItem.Mappings.Select(m => (IMarketMappingData) new MarketMapping(m)).ToList());

            VariantDescriptionCacheItem = cacheItem;
        }

        public void SetMappings(ReadOnlyCollection<IMarketMappingData> mappings)
        {
            Mappings = mappings;
        }
    }
}
