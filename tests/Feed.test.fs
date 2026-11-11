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

Vitest.test("formats due arrivals, whole minutes, and nonpositive headways", fun () ->
    Vitest.expect(formatRelative (createDateMs fixedNowMs)).toBe "due"
    Vitest.expect(formatRelative (createDateMs(fixedNowMs + 60000.0))).toBe "in 1 min"
    Vitest.expect(formatRelative (createDateMs(fixedNowMs + 3600000.0))).toBe "in 1h"
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
