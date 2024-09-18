using Microsoft.Extensions.Logging;
using NodaTime.Text;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace YahooQuotesApi;

internal static class TickParser //This would have to be adjusted so instead of CSV data, the JSON data is parsed
{
    private static readonly LocalDatePattern DatePattern = LocalDatePattern.CreateWithInvariantCulture("yyyy-MM-dd");

    internal static async Task<ITick[]> ToTicks<T>(this JsonDocument jsonDocument, ILogger logger) where T : ITick
    {
        // Sometimes currencies end with two rows having the same date.
        // Sometimes ticks are returned in seemingly random order.
        // So use a dictionary to clean data.
        Dictionary<LocalDate, ITick> ticks = new(0x100);

        //ITick[] ticks;
        var root = jsonDocument.RootElement;
        
        if (typeof(T) == typeof(PriceTick))
        {
            var timestamps = root.GetProperty("chart").GetProperty("result")[0].GetProperty("timestamp").EnumerateArray();
            var quote = root.GetProperty("chart").GetProperty("result")[0].GetProperty("indicators").GetProperty("quote")[0];
            //OHLC data
            var opens = quote.GetProperty("open").EnumerateArray();
            var highs = quote.GetProperty("high").EnumerateArray();
            var lows = quote.GetProperty("low").EnumerateArray();
            var closes = quote.GetProperty("close").EnumerateArray();
            var volumes = quote.GetProperty("volume").EnumerateArray();

            var adjCloses = root.GetProperty("chart").GetProperty("result")[0].GetProperty("indicators")
                .GetProperty("adjclose")[0].GetProperty("adjclose").EnumerateArray();

            var timeIter = timestamps.GetEnumerator();
            var openIter = opens.GetEnumerator();
            var highIter = highs.GetEnumerator();
            var lowIter = lows.GetEnumerator();
            var closeIter = closes.GetEnumerator();
            var volumeIter = volumes.GetEnumerator();
            var adjCloseIter = adjCloses.GetEnumerator();

            while (timeIter.MoveNext() && openIter.MoveNext() && highIter.MoveNext() &&
                lowIter.MoveNext() && closeIter.MoveNext() && volumeIter.MoveNext() && adjCloseIter.MoveNext())
            {
                // Convert timestamp to LocalDate
                var date = LocalDate.FromDateTime(ConvertFromUnixTimestamp(timeIter.Current.GetInt64()).DateTime);

                // Create a new PriceTick instance
                PriceTick priceTick = new PriceTick(
                    Date: date,
                    Open: Math.Round(openIter.Current.GetDouble(), 2),
                    High: Math.Round(highIter.Current.GetDouble(), 2),
                    Low: Math.Round(lowIter.Current.GetDouble(), 2),
                    Close: Math.Round(closeIter.Current.GetDouble(), 2),
                    AdjustedClose: Math.Round(adjCloseIter.Current.GetDouble(), 2),
                    Volume: volumeIter.Current.GetInt64()
                );

                // Add the PriceTick to the list
                if (ticks.TryGetValue(priceTick.Date, out ITick? tick1))
                    logger.LogInformation("Ticks have same date: {Tick1} => {Tick}", tick1, priceTick);

                ticks[priceTick.Date] = priceTick; //Add or update
            }
        }
        else if (typeof(T) == typeof(SplitTick))
        {
            //Splits data
            JsonElement splits = root.GetProperty("chart").GetProperty("result")[0].GetProperty("events")
                            .GetProperty("splits");//.EnumerateArray();

            foreach(JsonProperty element in splits.EnumerateObject())
            {
                JsonElement splitProp = element.Value;
                if(splitProp.TryGetProperty("date", out JsonElement propDate) && splitProp.TryGetProperty("numerator", out JsonElement after)
                    && splitProp.TryGetProperty("denominator", out JsonElement before) )
                {
                    LocalDate date = LocalDate.FromDateTime(ConvertFromUnixTimestamp(propDate.GetInt64()).DateTime);
                    
                    SplitTick splitTick = new SplitTick(
                        Date: date,
                        BeforeSplit: before.GetDouble(),
                        AfterSplit: after.GetDouble()
                        );

                    Console.WriteLine(splitTick.BeforeSplit);
                    ticks[splitTick.Date] = splitTick;
                }
                else
                {
                    throw new InvalidOperationException("Unable to find properties for DividendTick");
                }
            }
        }
        else if (typeof(T) == typeof(DividendTick))
        {
            //Dividends
            JsonElement dividend = root.GetProperty("chart").GetProperty("result")[0].GetProperty("events")
                            .GetProperty("dividends");

            foreach (JsonProperty element in dividend.EnumerateObject())
            {
                JsonElement splitProp = element.Value;
                if (splitProp.TryGetProperty("date", out JsonElement propDate) && splitProp.TryGetProperty("amount", out JsonElement amount)
                    )
                {
                    LocalDate date = LocalDate.FromDateTime(ConvertFromUnixTimestamp(propDate.GetInt64()).DateTime);

                    DividendTick dividendTick = new DividendTick(
                        Date: date,
                        Dividend: amount.GetDouble()
                        );

                    ticks[dividendTick.Date] = dividendTick;
                }
                else
                {
                    throw new InvalidOperationException("Unable to find properties for SplitTick");
                }
            }
        }
        else
        {
            throw new InvalidOperationException("Tick type.");
        }


        return ticks.Values.OrderBy(x => x.Date).ToArray();
    }

    private static DateTimeOffset ConvertFromUnixTimestamp(long timestamp)
    {
        return DateTimeOffset.FromUnixTimeSeconds(timestamp);
    }

    internal static LocalDate ToDate(this string str)
    {
        // NodaTime does not yet support Span<char>.
        ParseResult<LocalDate> result = DatePattern.Parse(str);
        if (result.Success)
            return result.Value;

        throw new InvalidDataException($"Could not convert '{str}' to LocalDate.", result.Exception);
    }

    internal static double ToDouble(this string str)
    {
        if (str == "null")
            return 0d;

        if (double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out double result))
            return result.RoundToSigFigs(7);

        throw new InvalidDataException($"Could not convert '{str}' to Double.");
    }

    internal static long ToLong(this string str)
    {
        if (str == "null")
            return 0L;

        if (long.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out long result))
            return result;

        throw new InvalidDataException($"Could not convert '{str}' to Long.");
    }
}
