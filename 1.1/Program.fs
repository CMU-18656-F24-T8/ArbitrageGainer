open System
open System.Net.Http
open Microsoft.FSharp.Core
open Suave
open Azure.Data.Tables
open Azure
open Suave.Filters
open Suave.Operators
open Suave.Successful
open Suave.RequestErrors
open Newtonsoft.Json

open Controller.RetrieveCrossTradedPair
open Controller.AnnualizedReturnCalculate
open Controller.RealtimeDataSocket

// TYPE DEFINITIONS

type TradingStrategy =
    { NumberOfCryptos: int
      MinPriceSpread: float
      MinTransactionProfit: float
      MaxTransactionValue: float
      MaxTradingValue: float
      InitialInvestment: float }

type MaxTradingValue = { MaxTradingValue: float }


type Email = { Email: string }

// Error handling

type Success = | Data

type Error = { Message: string; Code: int }

type Result<'Success, 'Error> =
    | Success of 'Success
    | Error of 'Error

// TABLE STORAGE ENTITY DEFINITIONS

type TradingStrategyEntity
    (
        numberOfCryptos: int,
        minPriceSpread: float,
        minTransactionProfit: float,
        maxTransactionValue: float,
        maxTradingValue: float,
        initialInvestment: float
    ) =
    interface ITableEntity with
        member val PartitionKey = "defaultPartition" with get, set
        member val RowKey = "defaultRow" with get, set
        member val ETag = ETag() with get, set
        member val Timestamp = Nullable<DateTimeOffset>() with get, set

    new() = TradingStrategyEntity(0, 0.0, 0.0, 0.0, 0.0, 0.0)

    member val NumberOfCryptos: int = numberOfCryptos with get, set
    member val MinPriceSpread: float = minPriceSpread with get, set
    member val MinTransactionProfit: float = minTransactionProfit with get, set
    member val MaxTransactionValue: float = maxTransactionValue with get, set
    member val MaxTradingValue: float = maxTradingValue with get, set
    member val InitialInvestment: float = initialInvestment with get, set

type EmailEntity(email: string) =
    interface ITableEntity with
        member val PartitionKey = "defaultPartition" with get, set
        member val RowKey = "emailRow" with get, set
        member val ETag = ETag() with get, set
        member val Timestamp = Nullable<DateTimeOffset>() with get, set

    new() = EmailEntity("")

    member val Email: string = email with get, set

// TABLE STORAGE FUNCTIONS

let connectToTable (storageConnString: string) (tableName: string) =
    let tableClient = TableServiceClient(storageConnString)
    let table = tableClient.GetTableClient(tableName)
    table.CreateIfNotExists() |> ignore
    table

let insertEntity (table: TableClient) (strategy: TradingStrategy) =
    try
        let entity =
            TradingStrategyEntity(
                strategy.NumberOfCryptos,
                strategy.MinPriceSpread,
                strategy.MinTransactionProfit,
                strategy.MaxTransactionValue,
                strategy.MaxTradingValue,
                strategy.InitialInvestment
            )

        table.AddEntity(entity) |> ignore
        Success()
    with
    | :? RequestFailedException as ex when ex.Status = 409 ->
        Error
            { Message = "Trading strategy already exists"
              Code = 409 }
    | _ ->
        Error
            { Message = "Internal server error"
              Code = 500 }

let updateMaxTradingValue (table: TableClient) (maxTradingValue: MaxTradingValue) =
    try
        match maxTradingValue with
        | { MaxTradingValue = 0.0 } ->
            Error
                { Message = "Max trading value cannot be 0 or empty"
                  Code = 400 }
        | _ ->
            let entity =
                table.GetEntity<TradingStrategyEntity>("defaultPartition", "defaultRow").Value

            entity.MaxTradingValue <- maxTradingValue.MaxTradingValue
            table.UpdateEntity(entity, ETag.All, TableUpdateMode.Replace) |> ignore
            Success()
    with
    | :? RequestFailedException as ex when ex.Status = 404 ->
        Error
            { Message = "Trading strategy not found"
              Code = 404 }
    | _ ->
        Error
            { Message = "Internal server error"
              Code = 500 }

let setEmail (table: TableClient) (email: Email) =
    try
        // check if email is valid
        System.Net.Mail.MailAddress(email.Email) |> ignore
        let entity = EmailEntity(email.Email)
        table.AddEntity(entity) |> ignore
        Success()
    with
    | :? RequestFailedException as ex when ex.Status = 409 ->
        Error
            { Message = "Email already exists"
              Code = 409 }
    | :? FormatException ->
        Error
            { Message = "Invalid email"
              Code = 400 }
    | _ ->
        Error
            { Message = "Internal server error"
              Code = 500 }

let getTradingStrategy (table: TableClient) =
    try
        let entity =
            table.GetEntity<TradingStrategyEntity>("defaultPartition", "defaultRow").Value

        Success
            { NumberOfCryptos = entity.NumberOfCryptos
              MinPriceSpread = entity.MinPriceSpread
              MinTransactionProfit = entity.MinTransactionProfit
              MaxTransactionValue = entity.MaxTransactionValue
              MaxTradingValue = entity.MaxTradingValue
              InitialInvestment = entity.InitialInvestment }
    with
    | :? RequestFailedException as ex when ex.Status = 404 ->
        Error
            { Message = "Trading strategy not found"
              Code = 404 }
    | _ ->
        Error
            { Message = "Internal server error"
              Code = 500 }

let getEmail (table: TableClient) =
    try
        let entity = table.GetEntity<EmailEntity>("defaultPartition", "emailRow").Value
        Success entity
    with
    | :? RequestFailedException as ex when ex.Status = 404 ->
        Error
            { Message = "Email not found"
              Code = 404 }
    | _ ->
        Error
            { Message = "Internal server error"
              Code = 500 }


// SERVER FUNCTIONS

let setTradingStrategyHandler (table: TableClient) =
    request (fun ctx ->
        let res =
            ctx.rawForm
            |> System.Text.Encoding.UTF8.GetString
            |> JsonConvert.DeserializeObject<TradingStrategy>
            |> insertEntity table

        match res with
        | Success _ -> OK "Trading strategy set"
        | Error error ->
            match error.Code with
            | 409 -> CONFLICT error.Message
            | _ -> ServerErrors.INTERNAL_ERROR error.Message)

let setMaxTradingValueHandler (table: TableClient) =
    request (fun ctx ->
        let res =
            ctx.rawForm
            |> System.Text.Encoding.UTF8.GetString
            |> JsonConvert.DeserializeObject<MaxTradingValue>
            |> updateMaxTradingValue table

        match res with
        | Success _ -> OK "Max trading value set"
        | Error error ->
            match error.Code with
            | 404 -> NOT_FOUND error.Message
            | _ -> ServerErrors.INTERNAL_ERROR error.Message)

let getTradingStrategyHandler (table: TableClient) =
    request (fun ctx ->
        let strategy = getTradingStrategy table

        match strategy with
        | Success strategy -> OK(JsonConvert.SerializeObject(strategy))
        | Error error ->
            match error.Code with
            | 404 -> NOT_FOUND error.Message
            | _ -> ServerErrors.INTERNAL_ERROR error.Message)

let setEmailHandler (table: TableClient) =
    request (fun ctx ->
        let res =
            ctx.rawForm
            |> System.Text.Encoding.UTF8.GetString
            |> JsonConvert.DeserializeObject<Email>
            |> setEmail table

        match res with
        | Success _ -> OK "Email set"
        | Error error ->
            match error.Code with
            | 409 -> CONFLICT error.Message
            | 400 -> BAD_REQUEST error.Message
            | _ -> ServerErrors.INTERNAL_ERROR error.Message)

let getEmailHandler (table: TableClient) =
    request (fun ctx ->
        let email = getEmail table

        match email with
        | Success email -> OK(JsonConvert.SerializeObject(email))
        | Error error ->
            match error.Code with
            | 404 -> NOT_FOUND error.Message
            | _ -> ServerErrors.INTERNAL_ERROR error.Message)

// SERVER SETUP

[<EntryPoint>]
let main argv =
    let connectionString =
        "DefaultEndpointsProtocol=https;AccountName=m2-1;AccountKey=MYUbtQC4VM5dZRJEIAXY2Km7dAPm3etmNn3qt1BUqtsyDpwrF1xlRCiFHt9ue1gygpImoBJBc43yACDb8WgO7A==;TableEndpoint=https://m2-1.table.cosmos.azure.com:443/;"

    let tableName = "functional_m2_1_table"
    let table = connectToTable connectionString tableName
    let httpClient = new HttpClient()
    let app =
        choose
            [
              POST >=> path "/trading_strategy" >=> setTradingStrategyHandler table
              GET >=> path "/trading_strategy" >=> getTradingStrategyHandler table
              POST >=> path "/email" >=> setEmailHandler table
              GET >=> path "/email" >=> getEmailHandler table
              PATCH >=> path "/trading_strategy" >=> setMaxTradingValueHandler table 
              GET >=> path "/crosstrade" >=> retrieveCrossTradedPairsHandler
              POST >=> path "/annualized_return" >=> calculateAnnualizedReturnHandler
            ]
    startWebServer defaultConfig app
    0
