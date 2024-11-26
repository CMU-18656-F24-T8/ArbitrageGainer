module Controller.RealtimeDataSocket

open System
open System.Net.WebSockets
open System.Text
open System.Text.Json
open System.Threading
open System.Text.Json.Serialization

// Domain types
type Exchange = Exchange of int
type CurrencyPair = CurrencyPair of string

type QuoteType = 
    | Ask
    | Bid

type CryptoQuote = {
    Pair: CurrencyPair
    Exchange: Exchange
    Type: QuoteType
    Size: decimal
    Price: decimal
}

// Discriminated union for WebSocket messages
type WebSocketMessage =
    | Text of string
    | Close
    | Other

// Types for Polygon messages
type PolygonMessage = {
    ev: string
    pair: string
    ap: decimal
    bp: decimal
    [<JsonPropertyName("as")>] _as: decimal
    bs: decimal
    x: int
    t: int64
}

// Immutable price cache
type PriceCache = {
    Quotes: Map<CurrencyPair * QuoteType, CryptoQuote>
}

// Result type for operations
type WebSocketResult<'T> =
    | Success of 'T
    | Error of string

// Module for cache operations
module QuoteCache =
    let empty = { Quotes = Map.empty }
    
    let updateQuote (cache: PriceCache) (quote: PolygonMessage) =
        let oldAsk = cache.Quotes.TryFind((CurrencyPair quote.pair, Ask))
        let oldBid = cache.Quotes.TryFind((CurrencyPair quote.pair, Bid))
        let updatedAsk =
           match oldAsk with
           | Some oldAsk when oldAsk.Price < quote.ap -> oldAsk  // if old exists and price is smaller, keep it
           | _ -> { Pair = CurrencyPair quote.pair; Exchange = Exchange quote.x; Type = Ask; Size = quote._as; Price = quote.ap }
        let updatedBid =
           match oldBid with
           | Some oldBid when oldBid.Price > quote.bp -> oldBid  // if old exists and price is higher, keep it
           | _ -> { Pair = CurrencyPair quote.pair; Exchange = Exchange quote.x; Type = Bid; Size = quote.bs; Price = quote.bp }
        { Quotes = cache.Quotes.Add((CurrencyPair quote.pair, Ask), updatedAsk).Add((CurrencyPair quote.pair, Bid), updatedBid) }
    
    let tryGetQuote (cache: PriceCache) (pair: CurrencyPair) (quoteType: QuoteType) =
        cache.Quotes.TryFind((pair, quoteType))
        
    let removeCachedQuote (cache: PriceCache) (pair: string option)=
        match pair with
        | Some pair -> 
            let pair = CurrencyPair pair
            { Quotes = cache.Quotes.Remove((pair, Ask)).Remove((pair, Bid)) }
        | None -> cache

// WebSocket connection module

open QuoteCache

module WebSocket =
    let connect (apiKey: string) (endpoint: string): Async<WebSocketResult<ClientWebSocket>> =
        async {
            let uri = Uri(endpoint)
            let wsClient = new ClientWebSocket()
            
            try
                do! wsClient.ConnectAsync(uri, CancellationToken.None) |> Async.AwaitTask
                let authMessage = sprintf """{"action":"auth","params":"%s"}""" apiKey
                let authMessageBytes = Encoding.UTF8.GetBytes(authMessage)
                do! wsClient.SendAsync(
                        new ArraySegment<byte>(authMessageBytes), 
                        WebSocketMessageType.Text, 
                        true, 
                        CancellationToken.None) |> Async.AwaitTask
                return Success wsClient
            with
            | ex -> return Error (sprintf "Connection failed: %s" ex.Message)
        }
    
    let subscribe (wsClient: ClientWebSocket) (pairs: CurrencyPair list) : Async<WebSocketResult<unit>> =
        async {
            try
                let pairsStr = 
                    pairs 
                    |> List.map (fun (CurrencyPair p) -> sprintf "XQ.%s" p) 
                    |> String.concat ","
                let subMessage = sprintf """{"action":"subscribe","params":"%s"}""" pairsStr
                let subMessageBytes = Encoding.UTF8.GetBytes(subMessage)
                do! wsClient.SendAsync(
                        new ArraySegment<byte>(subMessageBytes), 
                        WebSocketMessageType.Text, 
                        true, 
                        CancellationToken.None) |> Async.AwaitTask
                return Success ()
            with
            | ex -> return Error (sprintf "Subscription failed: %s" ex.Message)
        }
    
    let receiveMessage (wsClient: ClientWebSocket) : Async<WebSocketMessage> =
        async {
            let buffer = Array.zeroCreate 8192
            let segment = ArraySegment(buffer)
            let! result = wsClient.ReceiveAsync(segment, CancellationToken.None) |> Async.AwaitTask
            
            match result.MessageType with
            | WebSocketMessageType.Text ->
                return Text(Encoding.UTF8.GetString(buffer, 0, result.Count))
            | WebSocketMessageType.Close ->
                return Close
            | _ ->
                return Other
        }

// Market data processing module
module MarketData =
    let processMessage (message: string) : PolygonMessage list =
        try
            let quotes = JsonSerializer.Deserialize<PolygonMessage[]>(message)
            quotes
            |> Array.filter (fun q -> q.ev = "XQ")
            |> Array.toList
        with
        | ex -> 
            printfn "Error processing message: %s" ex.Message
            []

// Market data service module
module MarketDataService =
    type DataFeedState = {
        Cache: PriceCache
        WebSocket: ClientWebSocket option
        SubscribedPairs: CurrencyPair list
    }
    
    let initState = {
        Cache = QuoteCache.empty
        WebSocket = None
        SubscribedPairs = []
    }
    
    let subscribeToMarketData 
        (endpoint: string)
        (apiKey: string) 
        (pairs: CurrencyPair list) 
        (onQuoteReceived: (PolygonMessage * PriceCache) -> string option)
        : Async<unit> = // Changed return type to Async<unit>
        
        let rec processMarketData (wsClient: ClientWebSocket) (state: DataFeedState) =
            async {
                match! WebSocket.receiveMessage wsClient with
                | Text message ->
                    let quotes = MarketData.processMessage message
                    let newCache = 
                        quotes 
                        |> List.fold QuoteCache.updateQuote state.Cache
                    
                    // Notify about updates, along with the cached quote
                    let removedCache =
                      quotes
                      |> List.map (fun q -> (q, newCache))
                      |> List.map onQuoteReceived
                      |> List.fold QuoteCache.removeCachedQuote newCache
                    
                    return! processMarketData wsClient { state with Cache = removedCache }
                | Close ->
                    printfn "Market data feed connection closed."
                    ()  // Return unit instead of state
                | Other ->
                    return! processMarketData wsClient state
            }
        
        async {
            match! WebSocket.connect apiKey endpoint with
            | Success wsClient ->
                match! WebSocket.subscribe wsClient pairs with
                | Success _ ->
                    let initialState = { 
                        initState with 
                            WebSocket = Some wsClient
                            SubscribedPairs = pairs 
                    }
                    do! processMarketData wsClient initialState  // Changed to do!
                | Error err ->
                    printfn "Failed to subscribe to market data: %s" err
            | Error err ->
                printfn "Failed to connect to market data feed: %s" err
        }


