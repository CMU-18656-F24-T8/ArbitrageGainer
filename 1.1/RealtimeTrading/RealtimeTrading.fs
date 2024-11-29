module RealtimeTrading.RealtimeTrading
open System
open RealtimeTrading.HistoricalOpportunities
open RealtimeTrading.RealtimeDataSocket
open TradingStrategy.Infrastructure
open RealtimeTrading.orderPlacementHandler

// Get most arbitrage opportunities in the past

let strategy = getTradingStrategy strategyAgent
let pairs = getTopNOpportunities strategy.NumberOfCryptos

// Function to map Exchange integer to its corresponding name
let exchangeName (exchange: Exchange) =
    match exchange with
    | Exchange 1 -> "Bitfinex"
    | Exchange 2 -> "Kraken"
    | Exchange 3 -> "Bitstamp"
    | _ -> "Unknown Exchange"

// Convert CryptoQuote to CryptoQuoteOnTransact
let toCryptoQuoteOnTransact (quote: CryptoQuote) =
    match quote with
    | { Pair = CurrencyPair pair; Exchange = exchange; Type = quoteType; Size = size; Price = price } ->
        {
            Exchange = exchangeName exchange // Convert Exchange to name
            Pair = pair
            Size = float size
            Prices = float price
        }

// Convert CryptoQuoteOnTransact to CryptoQuote
let toCryptoQuote (transactQuote: CryptoQuoteOnTransact) (quoteType:QuoteType)=
    match transactQuote with
    | { Exchange = exchange; Pair = pair; Size = size; Prices = prices } ->
        let exchangeValue = 
            match exchange with
            | "Bitfinex" -> Exchange 1
            | "Kraken" -> Exchange 2
            | "Bitstamp" -> Exchange 3
            | _ -> Exchange 0 // Use a default value for unknown exchanges
        
        {
            Pair = CurrencyPair pair
            Exchange = exchangeValue // Convert exchange name to Exchange type
            Type = quoteType // Convert string to QuoteType
            Size = decimal size
            Price = decimal prices
        }

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
                
                orderHandler (toCryptoQuoteOnTransact highestBid) (toCryptoQuoteOnTransact lowestAsk) |> ignore
                
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



