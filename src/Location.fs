module Location

open System
open Fable.Core
open Config
open State

[<Emit("Number.isFinite($0)")>]
let private isFinite (v: float) : bool = jsNative

let private nearestStop (stopLookup: Map<string, StopOption>) (lat: float) (lon: float) : string option =
    if not (isFinite lat && isFinite lon) || abs lat > 90.0 || abs lon > 180.0 then
        None
    else
        let rad deg = deg * Math.PI / 180.0
        let haversine sLat sLon =
            (sin (rad (sLat - lat) / 2.0) ** 2.0)
            + cos (rad lat) * cos (rad sLat) * (sin (rad (sLon - lon) / 2.0) ** 2.0)

        PREFERRED_STOP_ORDER
        |> Array.choose (fun id ->
            match Map.tryFind id stopLookup with
            | Some { latitude = Some sLat; longitude = Some sLon } -> Some(id, haversine sLat sLon)
            | _ -> None)
        |> function
            | [||] -> None
            | candidates -> candidates |> Array.minBy snd |> fst |> Some

type IGeoCoords =
    abstract latitude: float
    abstract longitude: float

type IGeoPosition =
    abstract coords: IGeoCoords

[<Emit("Boolean(navigator.geolocation)")>]
let private hasGeolocation () : bool = jsNative

[<Emit("navigator.geolocation.getCurrentPosition($0, $1, { timeout: 8000, maximumAge: 60000 })")>]
let private getCurrentPosition (onSuccess: IGeoPosition -> unit) (onError: obj -> unit) : unit = jsNative

let nearestStopFromDevice (stopLookup: Map<string, StopOption>) () : Async<(string * (float * float)) option> =
    Async.FromContinuations(fun (resolve, _, _) ->
        if not (hasGeolocation ()) then
            resolve None
        else
            try
                getCurrentPosition
                    (fun pos ->
                        let lat = pos.coords.latitude
                        let lon = pos.coords.longitude
                        let result =
                            nearestStop stopLookup lat lon
                            |> Option.map (fun stopId -> stopId, (lat, lon))
                        resolve result)
                    (fun _ -> resolve None)
            with _ -> resolve None)
