module RealtimeTrading.Infrastructure
open Azure.Data.Tables
open Azure
type StatusMsg =
    | Start
    | Stop
    | GetStatus of AsyncReplyChannel<string>

let realTimeTradingStatusAgent = MailboxProcessor.Start(fun inbox ->
    let rec loop status =
        async {
            let! msg = inbox.Receive()
            match msg with
            | Start -> 
                printfn "Starting real-time trading"
                return! loop "started"
            | Stop -> 
                printfn "Stopping real-time trading"
                return! loop "stopped"
            | GetStatus channel -> 
                channel.Reply(status)
                return! loop status
        }
    loop "stopped"
)

let storageConnString = "DefaultEndpointsProtocol=https;AccountName=arbitrage-db;AccountKey=c8GMmbsHkT0iuejjzY0lmpXbAHSLvEE3bWdQM6xTX9yoZqtwqyzxUIqlj6EhnnHgQMMbGXtyBtSaACDbC9Y1rg==;TableEndpoint=https://arbitrage-db.table.cosmos.azure.com:443/;"

let tableClient = TableServiceClient storageConnString
let table = tableClient.GetTableClient "arbitrage"

table.CreateIfNotExists () |> ignore

// Function to write a JSON string to a table row
let WriteJsonToTableRow (partitionKey: string) (rowKey: string) (jsonString: string) =
    async {
        let entity = TableEntity()
        entity.Add("PartitionKey", partitionKey)
        entity.Add("RowKey", rowKey)
        entity.Add("JsonData", jsonString) // Store the JSON string in the "JsonData" column
        let res = table.UpsertEntity(entity, TableUpdateMode.Replace)
        match res.IsError with
        | true -> return Result.Error "Failed to insert JSON string"
        | false -> return Ok "JSON string inserted successfully"
    }

// Function to load a string from the database
let LoadStringFromDB (partitionKey: string) (rowKey: string) =
    async {
        try
            let response = table.GetEntity<TableEntity>(partitionKey, rowKey)
            let jsonData = response.Value.GetString("JsonData") // Retrieve the "JsonData" column value
            return Ok jsonData
        with
        | :? RequestFailedException as ex ->
            return Result.Error (sprintf "Failed to retrieve data: %s" ex.Message)
    }