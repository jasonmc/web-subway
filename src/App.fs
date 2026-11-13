module App

open System
open Fable.Core
open Fable.Core.JsInterop
open Elmish
open Config
open State
open Stops
open Location
open Feed
open Timers
open Ui
open Time

type IResponse =
    abstract ok: bool
    abstract status: int
    abstract statusText: string
    abstract arrayBuffer: unit -> JS.Promise<JS.ArrayBuffer>

[<Emit("import.meta.env.VITE_MTA_FEED_URL")>]
let private envFeedUrl: string option = jsNative

[<Emit("fetch($0, { mode: 'cors' })")>]
let private fetchCors (url: string) : JS.Promise<IResponse> = jsNative

let private fetchSnapshot () : Async<FeedSnapshot> =
    async {
        let! res = Async.AwaitPromise(fetchCors (envFeedUrl |? DEFAULT_FEED_URL))

        if not res.ok then
            return failwith $"Request failed: {res.status} {res.statusText}"

        let! buf = Async.AwaitPromise(res.arrayBuffer ())
        return decodeFeed buf |> parseSnapshot
    }

let private recomputeTrains
    (headerTimestampMs: float<milliseconds> option)
    (stopLookup: Map<string, StopOption>)
    (prevPositions: Map<string, float * float<seconds>>)
    (rawTrips: RawTrainTrip array)
    : EstimatedTrain array * Map<string, float * float<seconds>> =
    estimateTrainPositions (effectiveNowSec headerTimestampMs) stopLookup prevPositions rawTrips

let private visibleTotal (arrivals: ArrivalGroup) =
    min arrivals.Northbound.Length ARRIVALS_PER_DIRECTION
    + min arrivals.Southbound.Length ARRIVALS_PER_DIRECTION
    + min arrivals.Other.Length ARRIVALS_PER_DIRECTION

let private selectStation (stopId: string) (model: Model) =
    let arrivals =
        model.lastFeed |> Option.map (fun feed -> arrivalsForStop feed stopId)

    { model with
        selectedStopPrefix = stopId
        lastArrivals = arrivals
        lastVisibleTotal = arrivals |> Option.map visibleTotal |? 0 }

let private startTimersSub: Cmd<Msg> =
    Cmd.ofEffect (fun dispatch ->
        scheduleRefresh (fun () -> dispatch (RefreshFeed false)) 5000<milliseconds>
        scheduleAgeTicker (fun () -> dispatch TickAge) 1000<milliseconds>)

// Elmish Init
let init () : Model * Cmd<Msg> =
    let stopOptions, stopLookup, defaultStop, selectedStopPrefix, hasUrlStop =
        initStops ()

    let model =
        { stopOptions = stopOptions
          stopLookup = stopLookup
          defaultStop = defaultStop
          selectedStopPrefix = selectedStopPrefix
          hasUrlStop = hasUrlStop
          manuallySelected = false
          fetching = false
          availableStops = None
          lastFeed = None
          lastArrivals = None
          lastFeedMeta = None
          lastFeedTimestampMs = None
          lastVisibleTotal = 0
          rawTrips = [||]
          spinePosByTrip = Map.empty
          mapTrains = [||]
          userLocation = None
          spotlightTripId = None
          mobileView = ArrivalsView
          status = Loading }

    let geoCmd =
        if hasUrlStop then
            Cmd.none
        else
            Cmd.OfAsync.either (nearestStopFromDevice stopLookup) () GeoResolved (fun _ -> GeoResolved None)

    model,
    Cmd.batch
        [ Cmd.ofEffect (fun _ -> syncStopParam selectedStopPrefix)
          geoCmd
          Cmd.ofMsg (RefreshFeed true)
          startTimersSub ]

// Elmish Update
let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | SelectMobileView view -> { model with mobileView = view }, Cmd.none

    | SelectStop stopId ->
        { model with manuallySelected = true } |> selectStation stopId, Cmd.ofEffect (fun _ -> syncStopParam stopId)

    | GeoResolved(Some(stopId, coords)) when not model.manuallySelected && not model.hasUrlStop ->
        { model with
            userLocation = Some coords }
        |> selectStation stopId,
        Cmd.ofEffect (fun _ -> syncStopParam stopId)

    | GeoResolved(Some(_, coords)) ->
        { model with
            userLocation = Some coords },
        Cmd.none

    | GeoResolved None -> model, Cmd.none

    | RefreshFeed _ when model.fetching -> model, Cmd.none

    | RefreshFeed showLoading ->
        let nextStatus = if showLoading then Loading else model.status

        { model with
            fetching = true
            status = nextStatus },
        Cmd.OfAsync.either fetchSnapshot () (Ok >> FeedLoaded) (fun ex ->
            JS.console.error ex

            let msg =
                if String.IsNullOrEmpty ex.Message then
                    "Failed to load arrivals."
                else
                    ex.Message

            Error msg |> FeedLoaded)

    | FeedLoaded(Ok feed) ->
        let arrivals = arrivalsForStop feed model.selectedStopPrefix
        let meta = feed.meta

        let nextAvailable =
            if Set.isEmpty feed.availableStops then
                model.availableStops
            else
                Some feed.availableStops

        let nextMapTrains, nextSpinePositions =
            recomputeTrains meta.headerTimestampMs model.stopLookup model.spinePosByTrip feed.rawTrips

        { model with
            fetching = false
            availableStops = nextAvailable
            lastFeed = Some feed
            lastArrivals = Some arrivals
            lastFeedMeta = Some meta
            lastFeedTimestampMs = meta.headerTimestampMs
            lastVisibleTotal = visibleTotal arrivals
            rawTrips = feed.rawTrips
            spinePosByTrip = nextSpinePositions
            mapTrains = nextMapTrains
            status = FeedAge meta.headerTimestampMs },
        Cmd.none

    | FeedLoaded(Error message) ->
        { model with
            fetching = false
            status = ErrorMessage message },
        Cmd.none

    | SetSpotlight tripIdOpt ->
        if model.spotlightTripId = tripIdOpt then
            model, Cmd.none
        else
            { model with
                spotlightTripId = tripIdOpt },
            Cmd.none

    | ToggleSpotlight tripId ->
        let next =
            if model.spotlightTripId = Some tripId then
                None
            else
                Some tripId

        { model with spotlightTripId = next }, Cmd.none

    | TickAge ->
        let nextMapTrains, nextSpinePositions =
            recomputeTrains model.lastFeedTimestampMs model.stopLookup model.spinePosByTrip model.rawTrips

        { model with
            mapTrains = nextMapTrains
            spinePosByTrip = nextSpinePositions },
        Cmd.none
