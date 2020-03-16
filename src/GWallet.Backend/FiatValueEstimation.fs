﻿namespace GWallet.Backend

open System
open System.Net

open FSharp.Data

open GWallet.Backend.FSharpUtil.UwpHacks

module FiatValueEstimation =
    let private PERIOD_TO_CONSIDER_PRICE_STILL_FRESH = TimeSpan.FromMinutes 2.0

    type CoinCapProvider = JsonProvider<"""
    {
      "data": {
        "id": "bitcoin",
        "symbol": "BTC",
        "currencySymbol": "x",
        "type": "crypto",
        "rateUsd": "6444.3132749056076909"
      },
      "timestamp": 1536347871542
    }
    """>

    type PriceProvider =
        | CoinCap
        | CoinGecko

    let private QueryOnlineInternal currency (provider: PriceProvider): Async<Option<string*string>> = async {
        use webClient = new WebClient()
        let tickerName =
            match currency,provider with
            | Currency.BTC,_ -> "bitcoin"
            | Currency.LTC,_ -> "litecoin"
            | Currency.ETH,_ -> "ethereum"
            | Currency.ETC,_ -> "ethereum-classic"
            | Currency.DAI,_ -> "dai"
            // the API of CoinCap is not returning anything for "sai" even if the API from coingecko does
            | Currency.SAI,PriceProvider.CoinCap -> "dai"
            | Currency.SAI,_ -> "sai"

        try
            let baseUrl =
                match provider with
                | PriceProvider.CoinCap ->
                    SPrintF1 "https://api.coincap.io/v2/rates/%s" tickerName
                | PriceProvider.CoinGecko ->
                    SPrintF1 "https://api.coingecko.com/api/v3/simple/price?ids=%s&vs_currencies=usd" tickerName
            let uri = Uri baseUrl
            let task = webClient.DownloadStringTaskAsync uri
            let! res = Async.AwaitTask task
            return Some (tickerName,res)
        with
        | :? WebException ->
            return None
    }

    let private QueryCoinCap currency = async {
        let! maybeJson = QueryOnlineInternal currency PriceProvider.CoinCap
        match maybeJson with
        | None -> return None
        | Some (_, json) ->
            try
                let tickerObj = CoinCapProvider.Parse json
                return Some tickerObj.Data.RateUsd
            with
            | ex ->
                if currency = ETC then
                    // interestingly this can throw in CoinCap because retreiving ethereum-classic doesn't work...
                    return None
                else
                    return raise <| FSharpUtil.ReRaise ex
    }

    let private QueryCoinGecko currency = async {
        let! maybeJson = QueryOnlineInternal currency PriceProvider.CoinGecko
        match maybeJson with
        | None -> return None
        | Some (ticker, json) ->
            // try to parse this as an example: {"bitcoin":{"usd":7952.29}}
            let parsedJsonObj = FSharp.Data.JsonValue.Parse json
            let usdPrice =
                match parsedJsonObj.TryGetProperty ticker with
                | None -> failwithf "Could not pre-parse %s" json
                | Some innerObj ->
                    match innerObj.TryGetProperty "usd" with
                    | None -> failwithf "Could not parse %s" json
                    | Some value -> value.AsDecimal()
            return Some usdPrice
    }

    let private RetrieveOnline currency = async {
        let coinGeckoJob = QueryCoinGecko currency
        let coinCapJob = QueryCoinCap currency
        let bothJobs = FSharpUtil.AsyncExtensions.MixedParallel2 coinGeckoJob coinCapJob
        let! maybeUsdPriceFromCoinGecko, maybeUsdPriceFromCoinCap = bothJobs
        if maybeUsdPriceFromCoinCap.IsSome && currency = Currency.ETC then
            Infrastructure.ReportWarningMessage "Currency ETC can now be queried from CoinCap provider?"
        match maybeUsdPriceFromCoinGecko, maybeUsdPriceFromCoinCap with
        | None, None -> return None
        | Some usdPriceFromCoinGecko, None ->
            Caching.Instance.StoreLastFiatUsdPrice(currency, usdPriceFromCoinGecko)
            return Some usdPriceFromCoinGecko
        | None, Some usdPriceFromCoinCap ->
            Caching.Instance.StoreLastFiatUsdPrice(currency, usdPriceFromCoinCap)
            return Some usdPriceFromCoinCap
        | Some usdPriceFromCoinGecko, Some usdPriceFromCoinCap ->
            let average = (usdPriceFromCoinGecko + usdPriceFromCoinCap) / 2m
            Caching.Instance.StoreLastFiatUsdPrice(currency, average)
            return Some average
    }

    let UsdValue(currency: Currency): Async<MaybeCached<decimal>> = async {
        let maybeUsdPrice = Caching.Instance.RetrieveLastKnownUsdPrice currency
        match maybeUsdPrice with
        | NotAvailable ->
            let! maybeOnlineUsdPrice = RetrieveOnline currency
            match maybeOnlineUsdPrice with
            | None -> return NotFresh NotAvailable
            | Some value -> return Fresh value
        | Cached(someValue,someDate) ->
            if (someDate + PERIOD_TO_CONSIDER_PRICE_STILL_FRESH) > DateTime.UtcNow then
                return Fresh someValue
            else
                let! maybeOnlineUsdPrice = RetrieveOnline currency
                match maybeOnlineUsdPrice with
                | None ->
                    return NotFresh (Cached(someValue,someDate))
                | Some freshValue ->
                    return Fresh freshValue
    }

