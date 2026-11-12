module Tests.FeedTests

open System
open Vitest
open State
open Feed
open Tests.Fixtures
open Time
open Geography

Vitest.beforeEach (fun () -> resetBrowser "")

Vitest.test (
    "decodes a protobuf feed and groups only the selected station's arrivals",
    fun () ->
        let arrivals, meta = parseFeed (decodeFeed (arrivalsFeed ())) "G26"
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
        let arrivals, _ = parseFeed feed "G26"
        Vitest.expect(arrivals.Southbound |> Array.map _.tripId).toEqual [| Some "123_G..S" |]
        let trips = extractRawTrips feed
        let south = trips |> Array.find (fun trip -> trip.tripId = "123_G..S")
        Vitest.expect(south.direction).toEqual Southbound
        Vitest.expect(trips |> Array.exists (fun trip -> trip.tripId = "999_G..S")).toBe false
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

        let arrivals, _ = parseFeed feed "G26"
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

            let arrivals, _ = parseFeed feed station
            let all = Array.concat [ arrivals.Northbound; arrivals.Southbound; arrivals.Other ]
            Vitest.expect(all |> Array.map _.tripId).toEqual [| Some "departing" |]
            Vitest.expect(all[0].time).toEqual (instantFromSeconds (fixedNowSec + 180.0<seconds>))
            // The terminating trip is still useful at an earlier boarding stop.
            let upstream, _ = parseFeed feed "G26"

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

        let arrivals, _ = parseFeed feed "G26"
        Vitest.expect(arrivals.Northbound.Length).toBe 2
        Vitest.expect(arrivals.Northbound |> Array.map _.destinationStopId).toEqual [| Some "G22"; Some "G24" |]
)

Vitest.test (
    "interpolates between stations and keeps a moving train progressing forward",
    fun () ->
        let _, lookup, _, _, _ = Stops.initStops ()
        let trips = extractRawTrips (decodeFeed (trainsFeed ()))
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
            Vitest.expect(formatRelative (instantFromMilliseconds (fixedNowMs + secondsToMilliseconds seconds))).toBe
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
    "uses the JavaScript clock in seconds for train estimation",
    fun () -> Vitest.expect(effectiveNowSec (Some fixedNowMs)).toEqual fixedNowSec
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
