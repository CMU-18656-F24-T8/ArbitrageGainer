module Util.DAC

open Azure.Data.Tables
open Azure
open Microsoft.Azure.Documents
open Microsoft.FSharp.Reflection

let storageConnString = "DefaultEndpointsProtocol=https;AccountName=arbitrage-db;AccountKey=c8GMmbsHkT0iuejjzY0lmpXbAHSLvEE3bWdQM6xTX9yoZqtwqyzxUIqlj6EhnnHgQMMbGXtyBtSaACDbC9Y1rg==;TableEndpoint=https://arbitrage-db.table.cosmos.azure.com:443/;";

let tableClient = TableServiceClient storageConnString
let table = tableClient.GetTableClient "arbitrage"

table.CreateIfNotExists () |> ignore


let recordToTableEntity<'T> (PartitionKey: string)(record: 'T)(RowKey: string)=
    match FSharpType.IsRecord(typeof<'T>) with
    | true ->
        let entity = TableEntity()
        let fields = FSharpValue.GetRecordFields(record) |> Array.toList
        let fieldNames = FSharpType.GetRecordFields(typeof<'T>) |> Array.toList
        
        List.zip fieldNames fields
        |> List.iter (fun (fieldInfo, value) -> entity.Add(fieldInfo.Name, value))
        entity.Add("PartitionKey",PartitionKey)
        entity.Add("RowKey",RowKey)
        entity
    | false -> failwith "Provided type is not a record."

let UpsertTable<'T> (PartitionKey: string) (data:'T) (RowKey: string) =
    async {
        let entity = recordToTableEntity<'T> PartitionKey data RowKey
        // Perform the upsert operation asynchronously
        let res = table.UpsertEntity(entity,TableUpdateMode.Replace)
        match res.IsError with
        | true -> return Result.Error ("Bad value")
        | false -> return Ok ("Insert Successfully")
    }

let UpsertTableString (PartitionKey: string) (data: string) (RowKey: string) =
    async {
        let entity = TableEntity()
        entity.Add("Value", data)
        entity.Add("PartitionKey",PartitionKey)
        entity.Add("RowKey",RowKey)
        let res = table.UpsertEntity(entity,TableUpdateMode.Replace)
        match res.IsError with
        | true -> return Result.Error ("Bad value")
        | false -> return Ok ("Insert Successfully")
    }