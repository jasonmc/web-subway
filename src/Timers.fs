module Timers

open Fable.Core

[<Emit("window.setInterval($0, $1)")>]
let private setInterval (fn: unit -> unit) (ms: int) : int = jsNative

[<Emit("clearInterval($0)")>]
let private clearInterval (id: int) : unit = jsNative

let private refreshTimer = ref None
let private ageTimer = ref None

let scheduleRefresh (onTick: unit -> unit) (intervalMs: int) =
    Option.iter clearInterval refreshTimer.Value
    refreshTimer.Value <- Some(setInterval onTick intervalMs)

let scheduleAgeTicker (onTick: unit -> unit) (intervalMs: int) =
    Option.iter clearInterval ageTimer.Value
    ageTimer.Value <- Some(setInterval onTick intervalMs)
