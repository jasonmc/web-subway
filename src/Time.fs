module Time

open System
open Fable.Core

[<Measure>]
type seconds

[<Measure>]
type milliseconds

let inline secondsToMilliseconds (value: float<seconds>) : float<milliseconds> = value * 1000.0<milliseconds / seconds>

let inline millisecondsToSeconds (value: float<milliseconds>) : float<seconds> = value / 1000.0<milliseconds / seconds>

// JavaScript's clock reports Unix timestamps in milliseconds.
let nowMilliseconds () : float<milliseconds> =
    JS.Constructors.Date.now () |> LanguagePrimitives.FloatWithMeasure<milliseconds>

let instantFromMilliseconds (value: float<milliseconds>) : DateTimeOffset =
    DateTimeOffset.FromUnixTimeMilliseconds(int64 value)

let instantFromSeconds (value: float<seconds>) : DateTimeOffset =
    value |> secondsToMilliseconds |> instantFromMilliseconds

let instantToMilliseconds (instant: DateTimeOffset) : float<milliseconds> =
    float (instant.ToUnixTimeMilliseconds())
    |> LanguagePrimitives.FloatWithMeasure<milliseconds>

let nowInstant () : DateTimeOffset =
    nowMilliseconds () |> instantFromMilliseconds

type private IClockFormatter =
    abstract format: unixMilliseconds: float -> string

[<Emit("new Intl.DateTimeFormat('en-US', { timeZone: 'America/New_York', hour: 'numeric', minute: '2-digit', hour12: true })")>]
let private createNewYorkFormatter () : IClockFormatter = jsNative

let private newYorkFormatter = createNewYorkFormatter ()

let formatNewYorkTime (instant: DateTimeOffset) : string =
    newYorkFormatter.format (float (instant.ToUnixTimeMilliseconds()))
