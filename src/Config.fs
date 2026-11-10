module Config

open System.Text.RegularExpressions
open Fable.Core

[<AutoOpen>]
module Prelude =
    let inline (|?) (opt: 'T option) (fallback: 'T) : 'T = Option.defaultValue fallback opt
    let inline (<|>) (a: 'T option) (b: 'T option) : 'T option = Option.orElse b a

    [<Emit("Promise.resolve($0)")>]
    let promiseResolve (v: 'A) : JS.Promise<'A> = jsNative

    [<Emit("$0.then($1)")>]
    let promiseBind (p: JS.Promise<'A>) (f: 'A -> JS.Promise<'B>) : JS.Promise<'B> = jsNative

    type PromiseBuilder() =
        member _.Return(x: 'T) : JS.Promise<'T> = promiseResolve x
        member _.ReturnFrom(p: JS.Promise<'T>) : JS.Promise<'T> = p
        member _.Bind(p: JS.Promise<'A>, f: 'A -> JS.Promise<'B>) : JS.Promise<'B> = promiseBind p f
        member _.Zero() : JS.Promise<unit> = promiseResolve ()
        member _.Combine(p1: JS.Promise<unit>, p2: JS.Promise<'T>) : JS.Promise<'T> = promiseBind p1 (fun () -> p2)
        member _.Delay(f: unit -> JS.Promise<'T>) : JS.Promise<'T> = promiseBind (promiseResolve ()) f
        member _.Run(p: JS.Promise<'T>) : JS.Promise<'T> = p

    let promise = PromiseBuilder()

let DEFAULT_FEED_URL = "https://api-endpoint.mta.info/Dataservice/mtagtfsfeeds/nyct%2Fgtfs-g"
let DEFAULT_STOP_ID_PREFIX = "G26"
let STOP_SUFFIX_RE = Regex("[NS]$")
let ARRIVALS_PER_DIRECTION = 3

let PREFERRED_STOP_ORDER = [|
    "F27"; "F26"; "F25"; "F24"; "F23"; "F22"; "F21"; "F20"; "A42"
    "G36"; "G35"; "G34"; "G33"; "G32"; "G31"; "G30"; "G29"; "G28"; "G26"; "G24"; "G22"
|]
