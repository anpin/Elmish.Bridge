namespace Elmish.Remoting

open System
[<RequireQualifiedAccess>]
module Suave =
    open Suave
    open Suave.Filters
    open Suave.Operators
    open Suave.Sockets
    open Suave.Sockets.Control
    open Suave.WebSocket
    /// Suave's server used by `ServerProgram.runServerAtWith` and `ServerProgram.runServerAt`
    /// Creates a `WebPart`
    let server uri arg (program: ServerProgram<'arg,'model,'server,'originalclient,'client>) : WebPart=
        let ws (webSocket:WebSocket) _ =
            let hi = ServerHub.Initialize program.serverHub
            let inbox =
                Server.createMailbox
                    (fun s ->
                        let resp = s |> System.Text.Encoding.UTF8.GetBytes |> ByteSegment
                        webSocket.send Text resp true |> Async.Ignore)
                    hi arg program
            let skt =
              socket {
                let mutable loop = true
                while loop do
                    let! msg = webSocket.read()
                    match msg with
                    |Text, data, true ->
                        let str = UTF8.toString data
                        let msg : 'server = Server.read str
                        (S msg) |> program.mapMsg |> Server.Msg |> inbox.Post
                    | (Close, _, _) ->
                        let emptyResponse = [||] |> ByteSegment
                        do! webSocket.send Close emptyResponse true                        
                        loop <- false                        
                    | _ -> ()}
            async {
                let! result = skt
                match result with
                |Choice1Of2() -> ()
                |Choice2Of2(_) ->
                    hi.Remove ()
                    program.onDisconnection |> Option.iter (S >> program.mapMsg >> Server.Msg >> inbox.Post)
                return result                
            }
        path uri >=> handShake ws
