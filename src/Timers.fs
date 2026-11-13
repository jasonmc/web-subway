module Timers

open System
open Browser
open Elmish
open Time

let every (intervalMs: int<milliseconds>) (message: unit -> 'msg) : Subscribe<'msg> =
    fun dispatch ->
        let timer = window.setInterval ((fun () -> dispatch (message ())), int intervalMs)

        { new IDisposable with
            member _.Dispose() = window.clearInterval timer }
