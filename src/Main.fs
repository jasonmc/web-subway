module Main

open Fable.Core
open Fable.Core.JsInterop
open Config
open State
open Stops
open Location
open Feed
open Timers
open Ui

importSideEffects "./style.css"

type IResponse =
    abstract ok: bool
    abstract status: int
    abstract statusText: string
    abstract arrayBuffer: unit -> JS.Promise<JS.ArrayBuffer>

[<Emit("import.meta.env.VITE_MTA_FEED_URL")>]
let private envFeedUrl: string option = jsNative

[<Emit("fetch($0, { mode: 'cors' })")>]
let private fetchCors (url: string) : JS.Promise<IResponse> = jsNative

[<Emit("($0 instanceof Error ? $0.message : 'Failed to load arrivals.')")>]
let private errorMessage (err: obj) : string = jsNative

let private fetchAndParse (stopIdPrefix: string) () : JS.Promise<Set<string> * ArrivalGroup * FeedMeta> = promise {
    let! res = fetchCors (envFeedUrl |? DEFAULT_FEED_URL)
    if not res.ok then
        failwith $"Request failed: {res.status} {res.statusText}"
    let! buf = res.arrayBuffer()
    let feed = decodeFeed buf
    let available = stopsInFeed feed
    let arrivals, meta = parseFeed feed stopIdPrefix
    return available, arrivals, meta
}

let private startRefreshSub : Cmd<Msg> =
    Cmd.ofEffect (fun dispatch ->
        scheduleRefresh (fun () -> dispatch (RefreshFeed false)) 5000)

let private startTimersSub : Cmd<Msg> =
    Cmd.ofEffect (fun dispatch ->
        scheduleRefresh (fun () -> dispatch (RefreshFeed false)) 5000
        scheduleAgeTicker (fun () -> dispatch TickAge) 1000)

// Elmish Init
let init () : Model * Cmd<Msg> =
    let stopOptions, stopLookup, defaultStop, selectedStopPrefix, hasUrlStop = initStops ()
    let model = {
        stopOptions = stopOptions
        stopLookup = stopLookup
        defaultStop = defaultStop
        selectedStopPrefix = selectedStopPrefix
        hasUrlStop = hasUrlStop
        manuallySelected = false
        fetching = false
        availableStops = None
        lastArrivals = None
        lastFeedMeta = None
        lastFeedTimestampMs = None
        lastVisibleTotal = 0
        status = Loading
    }
    let geoCmd =
        if hasUrlStop then Cmd.none
        else Cmd.ofPromise (nearestStopFromDevice stopLookup) GeoResolved (fun _ -> GeoResolved None)

    model, Cmd.batch [ geoCmd; Cmd.ofMsg (RefreshFeed true); startTimersSub ]

// Elmish Update
let update (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | SelectStop stopId ->
        { model with selectedStopPrefix = stopId; manuallySelected = true },
        Cmd.batch [ Cmd.ofMsg (RefreshFeed true); startRefreshSub ]

    | GeoResolved (Some stopId) when not model.manuallySelected && not model.hasUrlStop ->
        { model with selectedStopPrefix = stopId },
        Cmd.ofMsg (RefreshFeed false)

    | GeoResolved _ ->
        model, Cmd.none

    | RefreshFeed _ when model.fetching ->
        model, Cmd.none

    | RefreshFeed showLoading ->
        let nextStatus = if showLoading then Loading else model.status
        { model with fetching = true; status = nextStatus },
        Cmd.ofPromise
            (fetchAndParse model.selectedStopPrefix)
            (Ok >> FeedLoaded)
            (fun err ->
                JS.console.error err
                Error (errorMessage err) |> FeedLoaded)

    | FeedLoaded (Ok (available, arrivals, meta)) ->
        let nextAvailable =
            if Set.isEmpty available then model.availableStops else Some available
        let visibleTotal =
            min arrivals.Northbound.Length ARRIVALS_PER_DIRECTION
            + min arrivals.Southbound.Length ARRIVALS_PER_DIRECTION
            + min arrivals.Other.Length ARRIVALS_PER_DIRECTION
        { model with
            fetching = false
            availableStops = nextAvailable
            lastArrivals = Some arrivals
            lastFeedMeta = Some meta
            lastFeedTimestampMs = meta.headerTimestampMs
            lastVisibleTotal = visibleTotal
            status = FeedAge meta.headerTimestampMs },
        Cmd.none

    | FeedLoaded (Error message) ->
        { model with fetching = false; status = ErrorMessage message },
        Cmd.none

    | TickAge ->
        let nextStatus =
            match model.lastFeedTimestampMs with
            | Some ts -> FeedAge (Some ts)
            | None -> model.status
        { model with status = nextStatus }, Cmd.none

// Elmish Program runner
let runProgram () =
    try
        let mutable currentModel, initialCmd = init ()
        view currentModel
        let rec dispatch (msg: Msg) =
            let nextModel, cmd = update msg currentModel
            currentModel <- nextModel
            view currentModel
            for effect in cmd do
                effect dispatch
        bindStopSelect dispatch
        for effect in initialCmd do
            effect dispatch
    with err ->
        JS.console.error err
        setError "Failed to load stops list."

runProgram ()
