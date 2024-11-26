module Util.DAC

open Azure.Data.Tables
open Azure
open Microsoft.FSharp.Reflection

let storageConnString = "DefaultEndpointsProtocol=https;AccountName=arbitrage-db;AccountKey=c8GMmbsHkT0iuejjzY0lmpXbAHSLvEE3bWdQM6xTX9yoZqtwqyzxUIqlj6EhnnHgQMMbGXtyBtSaACDbC9Y1rg==;TableEndpoint=https://arbitrage-db.table.cosmos.azure.com:443/;";

let tableClient = TableServiceClient storageConnString

let tableNames = [
    "constants";
    "transactionHistory"
]

let tables = tableNames |> List.map (fun tableName -> (tableName, tableClient.GetTableClient tableName))|> Map.ofList

tables|> Map.iter (fun tableName table -> table.CreateIfNotExists () |> ignore)

let recordToTableEntity<'T> (record: 'T) =
    match FSharpType.IsRecord(typeof<'T>) with
    | true ->
        let entity = TableEntity()
        let fields = FSharpValue.GetRecordFields(record) |> Array.toList
        let fieldNames = FSharpType.GetRecordFields(typeof<'T>) |> Array.toList
        
        List.zip fieldNames fields
        |> List.iter (fun (fieldInfo, value) -> entity.Add(fieldInfo.Name, value))
        entity
    | false -> failwith "Provided type is not a record."

let UpsertTable<'T> (tableName:string) (data:'T) =
    async {
        let entity = recordToTableEntity data
        // Perform the upsert operation asynchronously
        let res = tables.[tableName].UpsertEntity(entity,TableUpdateMode.Replace)
        match res.IsError with
        | true -> return Error ("Http Error")
        | false -> return Ok ("Insert Successfully")
    }
    
let retrieveTable