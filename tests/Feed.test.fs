module Tests.FeedTests

open System
open Vitest
open State
open Feed
open Tests.Fixtures

Vitest.beforeEach(fun () -> resetBrowser "")

Vitest.test("decodes a protobuf feed and groups only the selected station's arrivals", fun () ->
    let arrivals, meta = parseFeed (decodeFeed (arrivalsFeed())) "G26"
    Vitest.expect(arrivals.Northbound.Length).toBe 2
    Vitest.expect(arrivals.Southbound.Length).toBe 1
    Vitest.expect(arrivals.Other.Length).toBe 0
    Vitest.expect(arrivals.Northbound[0].time).toEqual(createDateMs(fixedNowMs + 120000.0))
    Vitest.expect(arrivals.Northbound[1].time).toEqual(createDateMs(fixedNowMs + 300000.0))
    Vitest.expect(meta.headerTimestampMs).toEqual(Some fixedNowMs))

Vitest.test("uses stop and trip directions when directionId disagrees", fun () ->
    let feed = decodeFeed (trainsFeed())
    let arrivals, _ = parseFeed feed "G26"
    Vitest.expect(arrivals.Southbound |> Array.map _.tripId).toEqual [| Some "123_G..S" |]
    let trips = extractRawTrips feed
    let south = trips |> Array.find (fun trip -> trip.tripId = "123_G..S")
    Vitest.expect(south.direction).toEqual Southbound
    Vitest.expect(trips |> Array.exists (fun trip -> trip.tripId = "999_G..S")).toBe false)

Vitest.test("interpolates between stations and keeps a moving train progressing forward", fun () ->
    let _, lookup, _, _, _ = Stops.initStops()
    let trips = extractRawTrips (decodeFeed (trainsFeed()))
    let initial, positions = estimateTrainPositions fixedNowSec lookup Map.empty trips
    let north = initial |> Array.find (fun train -> train.tripId = "G-N-1")
    Vitest.expect(north.latitude).toBeGreaterThan 40.724635
    Vitest.expect(north.latitude).toBeLessThan 40.731352
    let later, _ = estimateTrainPositions (fixedNowSec + 30.0) lookup positions trips
    let moved = later |> Array.find (fun train -> train.tripId = north.tripId)
    Vitest.expect(moved.latitude).toBeGreaterThan north.latitude
    Vitest.expect(moved.spinePos).toBeGreaterThan north.spinePos)

for seconds, expected in [
    -1.0, "due"
    0.0, "due"
    0.001, "<1 min"
    30.0, "<1 min"
    50.0, "<1 min"
    59.999, "<1 min"
    60.0, "in 1 min"
    90.0, "in 1 min"
    119.999, "in 1 min"
    120.0, "in 2 min"
    3599.999, "in 59 min"
    3600.0, "in 1h"
    3659.999, "in 1h"
    3660.0, "in 1h 1m"
] do
    Vitest.test($"formats an arrival {seconds} seconds away as {expected}", fun () ->
        Vitest.expect(formatRelative (createDateMs(fixedNowMs + seconds * 1000.0))).toBe expected)

Vitest.test("omits nonpositive headways", fun () ->
    let arrival: Arrival = {
        time = createDateMs fixedNowMs
        route = "G"
        direction = Northbound
        tripId = Some "one"
        label = None
    }
    Vitest.expect(headway arrival (Some arrival)).toBe ""
    Vitest.expect(headway arrival (Some { arrival with time = arrival.time.AddMinutes(-1.0) })).toBe "")

Vitest.test("generated station data supplies coordinates used for location and map placement", fun () ->
    let _, lookup, _, _, _ = Stops.initStops()
    Vitest.expect(lookup["G22"].latitude).toEqual(Some 40.746554)
    Vitest.expect(lookup["G22"].longitude).toEqual(Some -73.943832)
    Vitest.expect(lookup["G26"].latitude).toEqual(Some 40.731352))
