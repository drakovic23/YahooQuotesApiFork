﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NodaTime;

namespace YahooQuotesApi
{
    public sealed class YahooQuotesBuilder
    {
        private readonly ILogger Logger;
        private HistoryFlags HistoryFlags;
        private Instant HistoryStartDate = Instant.FromUtc(2000, 1, 1, 0, 0);
        private Frequency PriceHistoryFrequency = Frequency.Daily;
        private Duration HistoryCacheDuration = Duration.Zero;

        public YahooQuotesBuilder() : this(NullLogger.Instance) { }
        public YahooQuotesBuilder(ILogger logger) => Logger = logger;

        public YahooQuotesBuilder WithDividendHistory()
        {
            HistoryFlags |= HistoryFlags.DividendHistory;
            return this;
        }
        public YahooQuotesBuilder WithSplitHistory()
        {
            HistoryFlags |= HistoryFlags.SplitHistory;
            return this;
        }
        public YahooQuotesBuilder WithPriceHistory(Frequency frequency = Frequency.Daily)
        {
            HistoryFlags |= HistoryFlags.PriceHistory;
            PriceHistoryFrequency = frequency;
            return this;
        }

        public YahooQuotesBuilder HistoryStarting(Instant start)
        {
            HistoryStartDate = start;
            return this;
        }

        public YahooQuotesBuilder HistoryCache(Duration cacheDuration)
        {
            HistoryCacheDuration = cacheDuration;
            return this;
        }

        public YahooQuotes Build()
        {
            return new YahooQuotes(
                Logger,
                HistoryFlags,
                HistoryStartDate,
                HistoryCacheDuration,
                PriceHistoryFrequency);
        }
    }
}
