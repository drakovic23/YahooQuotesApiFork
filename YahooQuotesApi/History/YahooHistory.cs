﻿using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace YahooQuotesApi;

public sealed class YahooHistory
{
    private ILogger Logger { get; }
    private Instant Start { get; }
    private Frequency PriceHistoryFrequency { get; }
    private IHttpClientFactory HttpClientFactory { get; }
    private ParallelProducerCache<string, Result<ITick[]>> Cache { get; }

    public YahooHistory(ILogger logger, YahooQuotesBuilder builder, IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(builder, nameof(builder));
        Logger = logger;
        Start = builder.HistoryStartDate;
        PriceHistoryFrequency = builder.PriceHistoryFrequency;
        HttpClientFactory = httpClientFactory;
        Cache = new ParallelProducerCache<string, Result<ITick[]>>(builder.Clock, builder.HistoryCacheDuration);
    }

    internal async Task<Result<T[]>> GetTicksAsync<T>(Symbol symbol, CancellationToken ct) where T : ITick
    {
        if (symbol.IsCurrency)
            throw new ArgumentException($"Invalid symbol: '{symbol.Name}'.");
        Type type = typeof(T);
        Frequency frequency = type == typeof(PriceTick) ? PriceHistoryFrequency : Frequency.Daily;
        Uri uri = GetUri<T>(symbol.Name, frequency);
        string key = $"{symbol},{type.Name},{frequency.Name()}";
        try
        {
            Result<ITick[]> result = await Cache.Get(key, () => Produce<T>(uri, ct)).ConfigureAwait(false);
            return result.ToResult(v => v.Cast<T>().ToArray()); // returns a copy of an array (mutable shallow copy)
        }
        catch (Exception e)
        {
            Logger.LogCritical(e, "History error: {Message}.", e.Message);
            throw;
        }
    }

    private Uri GetUri<T>(string symbol, Frequency frequency) where T : ITick
    {
        string parm = typeof(T).Name switch
        {
            nameof(PriceTick) => "history",
            nameof(DividendTick) => "div",
            nameof(SplitTick) => "split",
            _ => throw new TypeAccessException("tick")
        };
        const string address = "https://query2.finance.yahoo.com/v8/finance/chart/"; //New uri
        string url = $"{address}{symbol}?period1={(Start == Instant.MinValue ? 0 : Start.ToUnixTimeSeconds())}" +
            $"&period2={Instant.MaxValue.ToUnixTimeSeconds()}&interval=1{frequency.Name()}&events={parm}";
        return new Uri(url);
    }

    private async Task<Result<ITick[]>> Produce<T>(Uri uri, CancellationToken ct) where T : ITick
    {
        Logger.LogInformation("{Url}", uri.ToString());

        HttpClient httpClient = HttpClientFactory.CreateClient("HttpV2");
        //httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/csv"));
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using HttpResponseMessage response = await httpClient.GetAsync(uri, ct).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return Result<ITick[]>.Fail("History not found.");
        try
        {
            response.EnsureSuccessStatusCode();
           
            using Stream stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            //JsonObject x = await response.Content.ReadFromJsonAsync();
            using JsonDocument jsonDocument = await JsonDocument.ParseAsync(stream,default,ct).ConfigureAwait(false);
            //using StreamReader streamReader = new(stream);
            //ITick[] ticks = await streamReader.ToTicks<T>(Logger).ConfigureAwait(false);

            ITick[] ticks = await jsonDocument.ToTicks<T>(Logger).ConfigureAwait(false);
            return ticks.ToResult();
        }
#pragma warning disable CA1031 // catch a more specific allowed exception type 
        catch (Exception e)
        {
            return Result<ITick[]>.Fail(e);
        }
    }
}
