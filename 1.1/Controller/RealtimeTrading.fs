module Controller.RealtimeTrading

open Controller.RealtimeDataSocket

open TradingStrategy.Infrastructure


// Get most arbitrage opportunities in the past
let getPairs = []  // TODO: Get pairs from historical data

// Handle quotes
let handleQuote quote =
    // Get trading strategy
    let strategy = getTradingStrategy strategyAgent
    // Handle incoming market data quotes
    let (msg: PolygonMessage), (cache) = quote
    let highestBid = QuoteCache.tryGetQuote cache (CurrencyPair msg.pair) Bid
    let lowestAsk = QuoteCache.tryGetQuote cache (CurrencyPair msg.pair) Ask
    match (highestBid, lowestAsk) with
    | Some highestBid, Some lowestAsk ->
        // printfn "Highest Bid: %A, Lowest Ask: %A" highestBid.Price lowestAsk.Price
        let spread = float (highestBid.Price - lowestAsk.Price)
        match spread with
        | spread when spread > strategy.MinPriceSpread ->  // Check if spread is greater than minimum price spread
            let maxTradingSize = float (min highestBid.Size lowestAsk.Size)
            let profit = float (spread * maxTradingSize)
            match profit with
            | profit when profit > strategy.MinTransactionProfit ->  // Check if profit is greater than minimum transaction profit
                let maxTradingValue = max strategy.MaxTradingValue strategy.MaxTransactionValue
                let maxTradingSizeByValue = maxTradingValue / float (highestBid.Price + lowestAsk.Price)
                let maxTradingSizeByAmount = float (min highestBid.Size lowestAsk.Size)
                let maxTradingSize = min maxTradingSizeByValue maxTradingSizeByAmount
                printfn "Orders Placed"
                printfn "%A, %A, %M, %f" msg.pair lowestAsk.Exchange lowestAsk.Price maxTradingSize
                printfn "%A, %A, %M, %f" msg.pair highestBid.Exchange highestBid.Price maxTradingSize
                // update total transaction value
                updateMaxTradingValue strategyAgent (strategy.MaxTradingValue - float (highestBid.Price + lowestAsk.Price) * maxTradingSize)
                // update cache
                Some msg.pair
            | _ -> None // printfn "Too small profit: %f" profit
        | _ -> None  // printfn "Too small spread"
    | _ -> None  // printfn "No enough data to calculate spread"

// Subscribe to market data
let subscribeToQuotes() =
    let apiKey = ""
    let endpoint = "wss://one8656-live-data.onrender.com/"
    MarketDataService.subscribeToMarketData endpoint apiKey getPairs handleQuote
    |> Async.Start


