module Timers

open Browser

let private refreshTimer = ref None
let private ageTimer = ref None

let scheduleRefresh (onTick: unit -> unit) (intervalMs: int) =
    Option.iter window.clearInterval refreshTimer.Value
    refreshTimer.Value <- Some(window.setInterval(onTick, intervalMs))

let scheduleAgeTicker (onTick: unit -> unit) (intervalMs: int) =
    Option.iter window.clearInterval ageTimer.Value
    ageTimer.Value <- Some(window.setInterval(onTick, intervalMs))
