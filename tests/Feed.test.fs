module Tests.FeedTests

open System
open Vitest
open State
open Feed
open Tests.Fixtures
open Time
open Geography

Vitest.beforeEach (fun () -> resetBrowser "")

for status, expectedPosition, stopped in [ 0, 18.95, false; 1, 19.0, true; 2, 18.5, false ] do
    Vitest.test (
        $"vehicle status {status} takes priority over predictions an hour in the future",
        fun () ->
            let feed =
                encode
                    [| trip "wrong-times" "reported" 0 [| call "G29N" 3600.0<seconds>; call "G28N" 3700.0<seconds> |]
                       vehicle "position" "reported" "G24N" status 1 -1200.0<seconds> |]
                |> decodeFeed

            let _, lookup, _, _, _ = Stops.initStops ()
            let trips = extractRawTrips fixedNow feed
            let trains, _ = estimateTrainPositions fixedNowSec lookup Map.empty trips
            Vitest.expect(trains.Length).toBe 1
            Vitest.expect(trains[0].nextStopId).toBe "G24"
            Vitest.expect(trains[0].spinePos).toBeCloseTo expectedPosition
            Vitest.expect(trains[0].isStopped).toBe stopped
    )

Vitest.test (
    "a stopped vehicle overrides expired predictions and previous interpolated positions",
    fun () ->
        let feed =
            encode
                [| trip "wrong-times" "stopped" 0 [| call "G26N" -3600.0<seconds>; call "G22N" -3300.0<seconds> |]
                   vehicle "position" "stopped" "G24N" 1 1 -1200.0<seconds> |]
            |> decodeFeed

        let _, lookup, _, _, _ = Stops.initStops ()
        let trips = extractRawTrips fixedNow feed
        let previous = Map.ofList [ "stopped", (20.0, fixedNowSec - 1.0<seconds>) ]
        let trains, positions = estimateTrainPositions fixedNowSec lookup previous trips
        Vitest.expect(trains.Length).toBe 1
        Vitest.expect(trains[0].spinePos).toBeCloseTo 19.0
        Vitest.expect(trains[0].isStopped).toBe true

        let later, _ =
            estimateTrainPositions (fixedNowSec + 30.0<seconds>) lookup positions trips

        Vitest.expect(later[0].spinePos).toBeCloseTo 19.0
        Vitest.expect(later[0].isStopped).toBe true

        let expired, _ =
            estimateTrainPositions (fixedNowSec + 301.0<seconds>) lookup positions trips

        Vitest.expect(expired.Length).toBe 0
)

Vitest.test (
    "in-transit reports constrain southbound interpolation without moving backwards at the prediction time",
    fun () ->
        let feed =
            encode
                [| trip "moving" "moving" 1 [| call "G24S" -30.0<seconds>; call "G26S" 90.0<seconds> |]
                   vehicle "position" "moving" "G26S" 2 1 -30.0<seconds> |]
            |> decodeFeed

        let _, lookup, _, _, _ = Stops.initStops ()
        let trips = extractRawTrips fixedNow feed

        let initial, positions =
            estimateTrainPositions (fixedNowSec + 60.0<seconds>) lookup Map.empty trips

        Vitest.expect(initial[0].spinePos).toBeGreaterThan 18.0
        Vitest.expect(initial[0].spinePos).toBeLessThan 19.0

        let atPrediction, positions =
            estimateTrainPositions (fixedNowSec + 90.0<seconds>) lookup positions trips

        Vitest.expect(atPrediction[0].spinePos).toBeLessThanOrEqual initial[0].spinePos
        Vitest.expect(atPrediction[0].isStopped).toBe false

        let afterPrediction, _ =
            estimateTrainPositions (fixedNowSec + 100.0<seconds>) lookup positions trips

        Vitest.expect(afterPrediction[0].spinePos).toBeLessThanOrEqual atPrediction[0].spinePos
        Vitest.expect(afterPrediction[0].spinePos).toBeGreaterThan 18.0
        Vitest.expect(afterPrediction[0].nextStopId).toBe "G26"
)

Vitest.test (
    "stale vehicle reports fall back to predictions",
    fun () ->
        let feed =
            encodeAt
                (fixedNowSec - 600.0<seconds>)
                [| trip "predictions" "reported" 0 [| call "G28N" -30.0<seconds>; call "G26N" 90.0<seconds> |]
                   vehicle "position" "reported" "G24N" 1 1 -600.0<seconds> |]
            |> decodeFeed

        let _, lookup, _, _, _ = Stops.initStops ()

        let trains, _ =
            estimateTrainPositions fixedNowSec lookup Map.empty (extractRawTrips fixedNow feed)

        Vitest.expect(trains.Length).toBe 1
        Vitest.expect(trains[0].nextStopId).toBe "G26"
        Vitest.expect(trains[0].spinePos).toBeLessThan 18.0
)

Vitest.test (
    "does not revive expired trips when parsing a stale feed",
    fun () ->
        let feed =
            encodeAt
                (fixedNowSec - 1860.0<seconds>)
                [| trip
                       "expired"
                       "expired"
                       0
                       [| call "G28N" -1890.0<seconds>
                          call "G26N" -1770.0<seconds>
                          call "G22N" -1530.0<seconds> |] |]
            |> decodeFeed

        Vitest.expect((extractRawTrips fixedNow feed).Length).toBe 0
)

Vitest.test (
    "decodes a protobuf feed and groups only the selected station's arrivals",
    fun () ->
        let arrivals, meta = parseFeed fixedNow (decodeFeed (arrivalsFeed ())) "G26"
        Vitest.expect(arrivals.Northbound.Length).toBe 2
        Vitest.expect(arrivals.Southbound.Length).toBe 1
        Vitest.expect(arrivals.Other.Length).toBe 0

        Vitest
            .expect(arrivals.Northbound[0].time)
            .toEqual (instantFromMilliseconds (fixedNowMs + 120000.0<milliseconds>))

        Vitest
            .expect(arrivals.Northbound[1].time)
            .toEqual (instantFromMilliseconds (fixedNowMs + 300000.0<milliseconds>))

        Vitest.expect(arrivals.Northbound[0].time.Offset).toEqual TimeSpan.Zero
        Vitest.expect(meta.headerTimestampMs).toEqual (Some fixedNowMs)
)

Vitest.test (
    "uses stop and trip directions when directionId disagrees",
    fun () ->
        let feed = decodeFeed (trainsFeed ())
        let arrivals, _ = parseFeed fixedNow feed "G26"
        Vitest.expect(arrivals.Southbound |> Array.map _.tripId).toEqual [| Some "123_G..S" |]
        let trips = extractRawTrips fixedNow feed
        let south = trips |> Array.find (fun trip -> trip.tripId = "123_G..S")
        Vitest.expect(south.direction).toEqual Southbound
        Vitest.expect(trips |> Array.exists (fun trip -> trip.tripId = "999_G..S")).toBe false
)

Vitest.test (
    "selects another station from the cached feed and omits arrivals that have passed",
    fun () ->
        let snapshot = parseSnapshot fixedNow (decodeFeed (trainsFeed ()))
        let initial = arrivalsForStop fixedNow snapshot "G26"
        Vitest.expect(initial.Northbound.Length).toBe 1
        let later = instantFromMilliseconds (fixedNowMs + 120000.0<milliseconds>)
        let current = arrivalsForStop later snapshot "G26"
        Vitest.expect(current.Northbound.Length).toBe 0
        Vitest.expect(current.Southbound.Length).toBe 1
        let otherStation = arrivalsForStop later snapshot "G24"
        Vitest.expect(otherStation.Northbound.Length).toBe 1
        Vitest.expect(otherStation.Northbound[0].destinationStopId).toEqual (Some "G22")
        Vitest.expect(otherStation.Southbound.Length).toBe 0
)

Vitest.test (
    "takes each trip's destination from its final stop, including short turns and reroutes",
    fun () ->
        let feed =
            encode
                [| trip "normal" "normal" 0 [| call "G26N" 120.0<seconds>; call "G22N" 360.0<seconds> |]
                   trip "short" "short" 0 [| call "G26N" 180.0<seconds>; call "G24N" 300.0<seconds> |]
                   trip "reroute" "reroute" 1 [| call "G26S" 240.0<seconds>; call "A42S" 900.0<seconds> |] |]
            |> decodeFeed

        let arrivals, _ = parseFeed fixedNow feed "G26"
        Vitest.expect(arrivals.Northbound |> Array.map _.destinationStopId).toEqual [| Some "G22"; Some "G24" |]
        Vitest.expect(arrivals.Southbound[0].destinationStopId).toEqual (Some "A42")
)

for station, terminalPlatform, departingPlatform, onwardPlatform, inboundDirection, outboundDirection in
    [ "G22", "G22N", "G22S", "G26S", 0, 1
      "F27", "F27S", "F27N", "G26N", 1, 0
      "G24", "G24N", "G24N", "G22N", 0, 0 ] do
    Vitest.test (
        $"omits trips ending at {station} while keeping departures and through trains",
        fun () ->
            let approach = if inboundDirection = 0 then "G26N" else "G26S"

            let feed =
                encode
                    [| trip
                           "terminating"
                           "terminating"
                           inboundDirection
                           [| call approach 60.0<seconds>; call terminalPlatform 120.0<seconds> |]
                       trip
                           "departing"
                           "departing"
                           outboundDirection
                           [| box
                                  {| stopId = departingPlatform
                                     departure = {| time = float (fixedNowSec + 180.0<seconds>) |} |}
                              box (call onwardPlatform 360.0<seconds>) |] |]
                |> decodeFeed

            let arrivals, _ = parseFeed fixedNow feed station
            let all = Array.concat [ arrivals.Northbound; arrivals.Southbound; arrivals.Other ]
            Vitest.expect(all |> Array.map _.tripId).toEqual [| Some "departing" |]
            Vitest.expect(all[0].time).toEqual (instantFromSeconds (fixedNowSec + 180.0<seconds>))
            // The terminating trip is still useful at an earlier boarding stop.
            let upstream, _ = parseFeed fixedNow feed "G26"

            let upstreamTrips =
                Array.concat [ upstream.Northbound; upstream.Southbound; upstream.Other ]

            Vitest.expect(upstreamTrips |> Array.map _.tripId).toEqual [| Some "terminating" |]
    )

Vitest.test (
    "uses terminal stops without predictions and ignores skipped or empty trailing stops",
    fun () ->
        let feed =
            encode
                [| trip "untimed" "untimed" 0 [| box (call "G26N" 120.0<seconds>); box {| stopId = "G22N" |} |]
                   trip
                       "skipped"
                       "skipped"
                       0
                       [| box (call "G26N" 180.0<seconds>)
                          box (call "G24N" 300.0<seconds>)
                          box
                              {| stopId = "G22N"
                                 scheduleRelationship = 1 |}
                          box {| stopId = "" |} |]
                   trip
                       "no-pickup"
                       "no-pickup"
                       0
                       [| box
                              {| stopId = "G26N"
                                 scheduleRelationship = 1
                                 arrival = {| time = float (fixedNowSec + 240.0<seconds>) |} |}
                          box (call "G22N" 420.0<seconds>) |] |]
            |> decodeFeed

        let arrivals, _ = parseFeed fixedNow feed "G26"
        Vitest.expect(arrivals.Northbound.Length).toBe 2
        Vitest.expect(arrivals.Northbound |> Array.map _.destinationStopId).toEqual [| Some "G22"; Some "G24" |]
)

Vitest.test (
    "interpolates between stations and keeps a moving train progressing forward",
    fun () ->
        let _, lookup, _, _, _ = Stops.initStops ()
        let trips = extractRawTrips fixedNow (decodeFeed (trainsFeed ()))
        let initial, positions = estimateTrainPositions fixedNowSec lookup Map.empty trips
        let north = initial |> Array.find (fun train -> train.tripId = "G-N-1")
        Vitest.expect(north.latitude).toBeGreaterThan 40.724635
        Vitest.expect(north.latitude).toBeLessThan 40.731352

        let later, _ =
            estimateTrainPositions (fixedNowSec + 30.0<seconds>) lookup positions trips

        let moved = later |> Array.find (fun train -> train.tripId = north.tripId)
        Vitest.expect(moved.latitude).toBeGreaterThan (float north.latitude)
        Vitest.expect(moved.spinePos).toBeGreaterThan north.spinePos
)

for seconds, expected in
    [ -1.0<seconds>, "due"
      0.0<seconds>, "due"
      0.001<seconds>, "<1 min"
      30.0<seconds>, "<1 min"
      50.0<seconds>, "<1 min"
      59.999<seconds>, "<1 min"
      60.0<seconds>, "in 1 min"
      90.0<seconds>, "in 1 min"
      119.999<seconds>, "in 1 min"
      120.0<seconds>, "in 2 min"
      3599.999<seconds>, "in 59 min"
      3600.0<seconds>, "in 1h"
      3659.999<seconds>, "in 1h"
      3660.0<seconds>, "in 1h 1m" ] do
    Vitest.test (
        $"formats an arrival {seconds} seconds away as {expected}",
        fun () ->
            Vitest
                .expect(formatRelative fixedNow (instantFromMilliseconds (fixedNowMs + secondsToMilliseconds seconds)))
                .toBe
                expected
    )

Vitest.test (
    "converts feed seconds to JavaScript milliseconds",
    fun () -> Vitest.expect(secondsToMilliseconds 1.25<seconds>).toEqual 1250.0<milliseconds>
)

Vitest.test (
    "converts JavaScript milliseconds to feed seconds",
    fun () -> Vitest.expect(millisecondsToSeconds 2500.0<milliseconds>).toEqual 2.5<seconds>
)

Vitest.test (
    "omits nonpositive headways",
    fun () ->
        let arrival: Arrival =
            { time = instantFromMilliseconds fixedNowMs
              route = "G"
              direction = Northbound
              tripId = Some "one"
              destinationStopId = None }

        Vitest.expect(headway arrival (Some arrival)).toBe ""

        Vitest
            .expect(
                headway
                    arrival
                    (Some
                        { arrival with
                            time = arrival.time.AddMinutes(-1.0) })
            )
            .toBe
            ""
)

Vitest.test (
    "generated station data supplies coordinates used for location and map placement",
    fun () ->
        let _, lookup, _, _, _ = Stops.initStops ()
        Vitest.expect(lookup["G22"].latitude).toEqual (Some 40.746554<latitude>)
        Vitest.expect(lookup["G22"].longitude).toEqual (Some -73.943832<longitude>)
        Vitest.expect(lookup["G26"].latitude).toEqual (Some 40.731352<latitude>)
)

Vitest.test (
    "projects the map's western and eastern corners without swapping coordinate axes",
    fun () ->
        let westX, westY = projectToMapPx 40.74835<latitude> -74.03<longitude>
        let eastX, eastY = projectToMapPx 40.74835<latitude> -73.909<longitude>
        Vitest.expect(westX).toBeCloseTo 0.0
        Vitest.expect(westY).toBeCloseTo 0.0
        Vitest.expect(eastX).toBeCloseTo MAP_WIDTH
        Vitest.expect(eastY).toBeCloseTo 0.0
)
