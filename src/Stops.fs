module Stops

open Browser
open Fable.Core
open Config
open State

[<ImportDefault("../stops.json")>]
// The generated JSON schema supplies latitude/longitude in decimal degrees.
let private stopOptions: StopOption array = jsNative

let private stopFromLocation () : string option =
    try
        match window.location.hash.TrimStart('#') with
        | ""
        | null -> None
        | h -> Some h
    with _ ->
        None

let initStops () =
    let stopLookup = stopOptions |> Array.map (fun s -> s.id, s) |> Map.ofArray

    let fallback =
        { id = DEFAULT_STOP_ID_PREFIX
          name = "Default stop"
          latitude = None
          longitude = None }

    let defaultStop =
        (stopOptions |> Array.tryFind (fun s -> s.id = DEFAULT_STOP_ID_PREFIX))
        <|> Array.tryHead stopOptions
        |? fallback

    let validUrlStop =
        stopFromLocation ()
        |> Option.filter (fun id -> stopOptions |> Array.exists (fun s -> s.id = id))

    let selectedStopPrefix = validUrlStop |? defaultStop.id
    stopOptions, stopLookup, defaultStop, selectedStopPrefix, Option.isSome validUrlStop

let stopLabel (stopLookup: Map<string, StopOption>) (stopId: string) : string =
    stopLookup |> Map.tryFind stopId |> Option.map _.name |? $"Stop {stopId}"
