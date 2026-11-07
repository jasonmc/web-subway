module Feed

open Fable.Core
open Config
open State

// Typed interfaces for gtfs-realtime-bindings (Option A)
type IStopTimeEvent = abstract time: obj option
type IStopTimeUpdate =
    abstract stopId: string option
    abstract arrival: IStopTimeEvent option
    abstract departure: IStopTimeEvent option
type ITripDescriptor =
    abstract routeId: string option
    abstract directionId: obj option
type ITripUpdate =
    abstract trip: ITripDescriptor option
    abstract stopTimeUpdate: IStopTimeUpdate array option
type IFeedEntity = abstract tripUpdate: ITripUpdate option
type IFeedHeader = abstract timestamp: obj option
type IFeedMessage =
    abstract header: IFeedHeader option
    abstract entity: IFeedEntity array
type IFeedMessageStatic = abstract decode: JS.Uint8Array -> IFeedMessage
type ITransitRealtime = abstract FeedMessage: IFeedMessageStatic
type IGtfsModule = abstract transit_realtime: ITransitRealtime

[<ImportAll("gtfs-realtime-bindings")>]
let private gtfs: IGtfsModule = jsNative

[<Emit("new Date()")>]
let createNowDate () : JS.Date = jsNative

[<Emit("new Date($0)")>]
let createDateMs (ms: float) : JS.Date = jsNative

[<Emit("Math.round($0)")>]
let private jsRound (value: float) : int = jsNative

let decodeFeed (buf: JS.ArrayBuffer) : IFeedMessage =
    gtfs.transit_realtime.FeedMessage.decode(JS.Constructors.Uint8Array.Create(buf))

// Coerce protobuf Long/number/undefined to positive epoch seconds.
[<Emit("(typeof $0 === 'number' ? $0 : typeof $0 === 'bigint' ? Number($0) : ($0 && typeof $0 === 'object' && 'toNumber' in $0 ? (() => { try { return $0.toNumber(); } catch { return undefined; } })() : undefined))")>]
let private toInt (value: obj option) : float option = jsNative

let private (|EventTime|_|) (ev: IStopTimeEvent option) : float option =
    ev |> Option.bind (fun e -> toInt e.time) |> Option.filter ((<>) 0.0)

let stopsInFeed (feed: IFeedMessage) : Set<string> =
    [| for entity in feed.entity do
           match entity.tripUpdate with
           | Some tu ->
               for stu in (tu.stopTimeUpdate |? [||]) do
                   match stu.stopId with
                   | Some id when id <> "" -> STOP_SUFFIX_RE.Replace(id, "")
                   | _ -> ()
           | None -> () |]
    |> Set.ofArray

let private formatHoursMinutes (m: int) (suffix: string) =
    match m / 60, m % 60 with
    | h, 0 -> $"{h}h{suffix}"
    | h, min -> $"{h}h {min}m{suffix}"

let formatRelative (target: JS.Date) : string =
    match jsRound ((target.getTime() - (createNowDate ()).getTime()) / 60000.0) with
    | m when m <= 0 -> "due"
    | 1 -> "in 1 min"
    | m when m < 60 -> $"in {m} min"
    | m -> "in " + formatHoursMinutes m ""

let formatAge (target: JS.Date) : string =
    match jsRound (((createNowDate ()).getTime() - target.getTime()) / 1000.0) with
    | s when s <= 0 -> "just now"
    | s when s < 60 -> $"{s}s ago"
    | s ->
        match jsRound (float s / 60.0) with
        | 1 -> "1 min ago"
        | m when m < 60 -> $"{m} min ago"
        | m -> formatHoursMinutes m " ago"

let headway (current: Arrival) (next: Arrival option) : string =
    match next with
    | Some n ->
        match jsRound ((n.time.getTime() - current.time.getTime()) / 60000.0) with
        | m when m > 0 -> $" • next in {m}m"
        | _ -> ""
    | None -> ""

let parseFeed (feed: IFeedMessage) (stopIdPrefix: string) : ArrivalGroup * FeedMeta =
    let nowMs = (createNowDate ()).getTime()
    let headerTimestampMs =
        feed.header
        |> Option.bind (fun h -> toInt h.timestamp)
        |> Option.filter ((<>) 0.0)
        |> Option.map ((*) 1000.0)

    let allArrivals =
        [| for entity in feed.entity do
               match entity.tripUpdate with
               | None -> ()
               | Some tu ->
                   let route = tu.trip |> Option.bind _.routeId |? "?"
                   let baseDir =
                       match tu.trip |> Option.bind (fun t -> toInt t.directionId) with
                       | Some 0.0 -> Northbound
                       | Some 1.0 -> Southbound
                       | _ -> Other

                   for stu in (tu.stopTimeUpdate |? [||]) do
                       match stu.stopId with
                       | Some stopId when stopId.StartsWith(stopIdPrefix) ->
                           match stu.arrival, stu.departure with
                           | EventTime ts, _ | _, EventTime ts ->
                               let whenDate = createDateMs (ts * 1000.0)
                               if whenDate.getTime() >= nowMs then
                                   let dir =
                                       if stopId.EndsWith("N") then Northbound
                                       elif stopId.EndsWith("S") then Southbound
                                       else baseDir
                                   { time = whenDate; route = route; direction = dir; label = None }
                           | _ -> ()
                       | _ -> () |]
        |> Array.sortBy (fun a -> a.time.getTime())

    let byDir d = allArrivals |> Array.filter (fun a -> a.direction = d)
    { Northbound = byDir Northbound; Southbound = byDir Southbound; Other = byDir Other },
    { headerTimestampMs = headerTimestampMs }
