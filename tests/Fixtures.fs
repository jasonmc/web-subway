module Tests.Fixtures

open System
open Browser.Dom
open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Feliz.UseElmish
open Vitest
open Time

let vi = Vitest.vi

let fixedNowMs =
    JS.Constructors.Date.parse "2024-01-01T12:00:00Z"
    |> LanguagePrimitives.FloatWithMeasure<milliseconds>

let fixedNowSec = millisecondsToSeconds fixedNowMs

type private IWriter =
    abstract finish: unit -> JS.Uint8Array

type private ICodec =
    abstract create: obj -> obj
    abstract encode: obj -> IWriter

type private ITransitRealtime =
    abstract FeedMessage: ICodec

type private IGtfs =
    abstract transit_realtime: ITransitRealtime

[<ImportDefault("gtfs-realtime-bindings")>]
let private gtfs: IGtfs = jsNative

let call stop (offset: float<seconds>) =
    {| stopId = stop
       arrival = {| time = float (fixedNowSec + offset) |} |}

let trip id tripId direction calls =
    createObj
        [ "id" ==> id
          "tripUpdate"
          ==> {| trip =
                  {| tripId = tripId
                     routeId = "G"
                     directionId = direction |}
                 stopTimeUpdate = calls |} ]

let vehicle id tripId stop status sequence (offset: float<seconds>) =
    createObj
        [ "id" ==> id
          "vehicle"
          ==> {| trip = {| tripId = tripId; routeId = "G" |}
                 stopId = stop
                 currentStatus = status
                 currentStopSequence = sequence
                 timestamp = float (fixedNowSec + offset) |} ]

let encodeAt (timestamp: float<seconds>) entities =
    let message =
        gtfs.transit_realtime.FeedMessage.create
            {| header =
                {| gtfsRealtimeVersion = "2.0"
                   timestamp = float timestamp |}
               entity = entities |}

    let bytes = gtfs.transit_realtime.FeedMessage.encode(message).finish ()
    bytes.buffer.slice (bytes.byteOffset, bytes.byteOffset + bytes.byteLength)

let encode entities = encodeAt fixedNowSec entities

let arrivalsFeed () =
    encode
        [| trip "north-1" "north-1" 0 [| call "G26N" 120.0<seconds>; call "G22N" 360.0<seconds> |]
           trip "north-2" "north-2" 0 [| call "G26N" 300.0<seconds>; call "G22N" 540.0<seconds> |]
           trip "south-1" "south-1" 1 [| call "G26S" 420.0<seconds>; call "F27S" 1800.0<seconds> |]
           trip "other-stop" "other-stop" 0 [| call "ZZZ" 60.0<seconds> |] |]

let trainsFeed () =
    encode
        [| trip
               "north-train"
               "G-N-1"
               0
               [| call "G28N" -30.0<seconds>
                  call "G26N" 90.0<seconds>
                  call "G24N" 210.0<seconds>
                  call "G22N" 330.0<seconds> |]
           // The stop suffix and trip ID must override this misleading directionId.
           trip
               "south-train"
               "123_G..S"
               0
               [| call "G24S" 80.0<seconds>
                  call "G26S" 200.0<seconds>
                  call "F27S" 1800.0<seconds> |]
           vehicle "south-train-veh" "123_G..S" "G22S" 1 1 -35.0<seconds>
           trip "unstarted-future" "999_G..S" 0 [| call "G22S" 600.0<seconds>; call "G24S" 660.0<seconds> |]
           vehicle "unstarted-future-veh" "999_G..S" "G22S" 2 0 600.0<seconds> |]

[<Emit("new Response($0, { status: $1, statusText: $1 === 200 ? 'OK' : 'fail' })")>]
let response (buffer: JS.ArrayBuffer) (status: int) : App.IResponse = jsNative

let mockFeed buffer =
    let fetch =
        vi.fn (fun (_: string) -> JS.Constructors.Promise.resolve (response buffer 200))

    vi.stubGlobal ("fetch", fetch)
    fetch

type GeoOptions =
    abstract timeout: int

type GeoSuccess = Location.IGeoPosition -> unit
type GeoError = obj -> unit
type GeoRequest = GeoSuccess -> GeoError -> GeoOptions -> unit

let mockLocation (request: GeoRequest option) =
    let geo =
        match request with
        | None -> null
        | Some getPosition ->
            createObj [ "getCurrentPosition" ==> Action<GeoSuccess, GeoError, GeoOptions>(getPosition) ]

    vi.stubGlobal ("navigator", createObj [ "userAgent" ==> "vitest"; "geolocation" ==> geo ])

let position latitude longitude : Location.IGeoPosition =
    unbox
        {| coords =
            {| latitude = latitude
               longitude = longitude |} |}

let resetBrowser hash =
    // Keep real timeouts for async rendering; control the clock and refresh intervals.
    vi.useFakeTimers (createObj [ "toFake" ==> [| "Date"; "setInterval"; "clearInterval" |] ])
    |> ignore

    vi.setSystemTime ((instantFromMilliseconds fixedNowMs).UtcDateTime)
    window.history.replaceState (null, "", "/" + hash)
    mockLocation None

[<ReactComponent>]
let private TestApp () =
    let model, dispatch = React.useElmish (App.init, App.update, [||])
    Ui.view model dispatch

let renderApp () = RTL.render (TestApp())

let waitForArrivals () =
    RTL.waitFor (fun () -> Vitest.expect(document.querySelector "#lists .arrival").toBeInTheDocument ())

let advance (duration: int<milliseconds>) =
    RTL.act (fun () -> vi.advanceTimersByTimeAsync (int duration))

let stationSelect () =
    RTL.screen.getByRole ("combobox", ByRoleOptions(name = Text "Station"))

let selectStation id =
    RTL.fireEvent.change (stationSelect (), createObj [ "target" ==> {| value = id |} ])
