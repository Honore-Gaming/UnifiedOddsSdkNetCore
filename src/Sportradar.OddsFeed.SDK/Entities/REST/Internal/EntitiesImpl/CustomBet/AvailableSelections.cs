﻿/*
* Copyright (C) Sportradar AG. See LICENSE for full license governing this code
*/
using Sportradar.OddsFeed.SDK.Entities.REST.Internal.DTO.CustomBet;
using Sportradar.OddsFeed.SDK.Messages;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sportradar.OddsFeed.SDK.Entities.REST.Internal.EntitiesImpl.CustomBet
{
    /// <summary>
    /// Implements methods used to access available selections for the event
    /// </summary>
    internal class AvailableSelections : REST.CustomBet.IAvailableSelections
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AvailableSelections"/> class
        /// </summary>
        /// <param name="availableSelections">a <see cref="AvailableSelectionsDto"/> representing the available selections</param>
        internal AvailableSelections(AvailableSelectionsDto availableSelections)
        {
            if (availableSelections == null)
            {
                throw new ArgumentNullException(nameof(availableSelections));
            }

            Event = availableSelections.Event;
            Markets = availableSelections.Markets.Select(m => new Market(m));
        }

        public URN Event { get; }

        public IEnumerable<REST.CustomBet.IMarket> Markets { get; }
    }
}
