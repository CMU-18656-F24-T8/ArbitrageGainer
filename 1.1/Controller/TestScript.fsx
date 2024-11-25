open System.Text.Json.Nodes
open FSharp.Data


let x = """
["Tom",4,{"college":"cmu"} ]
"""

let n = JsonValue.Parse(x)

n.[0]

n.[2].["college"].GetType

n.[0].GetType()