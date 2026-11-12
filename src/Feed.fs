module Feed

open System
open Fable.Core
open Config
open State
open Time
open Geography

let MAP_WIDTH = 440.0
let MAP_HEIGHT = 510.0

let private mercatorY (lat: float<latitude>) =
    Math.Log(Math.Tan((90.0 + float lat) * Math.PI / 360.0)) * (180.0 / Math.PI)

let private LON_MIN, LON_MAX, LAT_MAX =
    -74.03<longitude>, -73.909<longitude>, 40.74835<latitude>

let private Y_MAX = mercatorY LAT_MAX
let private MAP_SCALE = MAP_WIDTH / float (LON_MAX - LON_MIN)

let projectToMapPx (lat: float<latitude>) (lon: float<longitude>) : float * float =
    float (lon - LON_MIN) * MAP_SCALE, (Y_MAX - mercatorY lat) * MAP_SCALE

let private mapPxToLatLon (px: float) (py: float) : float<latitude> * float<longitude> =
    let lat =
        (2.0 * Math.Atan(Math.Exp((Y_MAX - py / MAP_SCALE) * Math.PI / 180.0))
         - Math.PI / 2.0)
        * (180.0 / Math.PI)

    LanguagePrimitives.FloatWithMeasure<latitude> lat,
    LON_MIN + LanguagePrimitives.FloatWithMeasure<longitude>(px / MAP_SCALE)

[<Emit("($0).toFixed($1)")>]
let toFixed (value: float) (digits: int) : string = jsNative

let pxToMapPercent (px: float) (py: float) : string * string =
    toFixed ((px / MAP_WIDTH) * 100.0) 3 + "%", toFixed ((py / MAP_HEIGHT) * 100.0) 3 + "%"

let projectToMapPercent (lat: float<latitude>) (lon: float<longitude>) : string * string =
    let px, py = projectToMapPx lat lon
    pxToMapPercent px py

[<ImportDefault("../g_corridor.svg?raw")>]
let STATIC_BASE_SVG: string = jsNative

let private clamp (lo: float<'u>) (hi: float<'u>) (v: float<'u>) = max lo (min hi v)

// Parse consecutive ((x0, y0, s0), (x1, y1, s1)) segments from <path class="osm-track-line" d="..."/> in g_corridor.svg
let private trackSegments: ((float * float * float) * (float * float * float)) array =
    let m =
        Text.RegularExpressions.Regex.Match(STATIC_BASE_SVG, "osm-track-line\" d=\"([^\"]+)\"")

    if not m.Success then
        [||]
    else
        let pts =
            Text.RegularExpressions.Regex.Matches(m.Groups[1].Value, "-?\\d+(?:\\.\\d+)?")
            |> Seq.cast<Text.RegularExpressions.Match>
            |> Seq.map (fun x -> float x.Value)
            |> Seq.chunkBySize 2
            |> Seq.choose (function
                | [| x; y |] -> Some(x, y)
                | _ -> None)
            |> Seq.toArray

        if pts.Length < 2 then
            [||]
        else
            let x0, y0 = pts[0]

            pts[1..]
            |> Array.scan
                (fun (px, py, s) (x, y) ->
                    let dx, dy = x - px, y - py
                    x, y, s + Math.Sqrt(dx * dx + dy * dy))
                (x0, y0, 0.0)
            |> Array.pairwise

let private projectOntoTrack (px: float, py: float) : float =
    if Array.isEmpty trackSegments then
        0.0
    else
        trackSegments
        |> Array.map (fun ((x0, y0, s0), (x1, y1, s1)) ->
            let vx, vy = x1 - x0, y1 - y0
            let l2 = vx * vx + vy * vy

            let t =
                if l2 > 0.0 then
                    clamp 0.0 1.0 (((px - x0) * vx + (py - y0) * vy) / l2)
                else
                    0.0

            let dx, dy = px - (x0 + t * vx), py - (y0 + t * vy)
            dx * dx + dy * dy, s0 + t * (s1 - s0))
        |> Array.minBy fst
        |> snd

let private pointAtArc (s: float) : float * float =
    let (x0, y0, s0), (x1, y1, s1) =
        trackSegments |> Array.tryFind (fun (_, (_, _, s1)) -> s1 >= s)
        |? Array.last trackSegments

    let t =
        if s1 > s0 then
            clamp 0.0 1.0 ((s - s0) / (s1 - s0))
        else
            0.0

    x0 + t * (x1 - x0), y0 + t * (y1 - y0)

// Typed interfaces for gtfs-realtime-bindings (Option A)
type IStopTimeEvent =
    abstract time: obj option

type IStopTimeUpdate =
    abstract stopId: string option
    abstract arrival: IStopTimeEvent option
    abstract departure: IStopTimeEvent option

type ITripDescriptor =
    abstract tripId: string option
    abstract routeId: string option
    abstract directionId: obj option

type ITripUpdate =
    abstract trip: ITripDescriptor option
    abstract stopTimeUpdate: IStopTimeUpdate array option

type IVehiclePosition =
    abstract trip: ITripDescriptor option
    abstract stopId: string option
    abstract currentStopSequence: obj option
    abstract timestamp: obj option

type IFeedEntity =
    abstract id: string option
    abstract tripUpdate: ITripUpdate option
    abstract vehicle: IVehiclePosition option

type IFeedHeader =
    abstract timestamp: obj option

type IFeedMessage =
    abstract header: IFeedHeader option
    abstract entity: IFeedEntity array

type IFeedMessageStatic =
    abstract decode: JS.Uint8Array -> IFeedMessage

type ITransitRealtime =
    abstract FeedMessage: IFeedMessageStatic

type IGtfsModule =
    abstract transit_realtime: ITransitRealtime

[<ImportAll("gtfs-realtime-bindings")>]
let private gtfs: IGtfsModule = jsNative

let private roundInt (value: float) : int =
    int (Math.Round(value, MidpointRounding.AwayFromZero))

let decodeFeed (buf: JS.ArrayBuffer) : IFeedMessage =
    gtfs.transit_realtime.FeedMessage.decode (JS.Constructors.Uint8Array.Create(buf))

// Coerce protobuf Long/number/undefined to a number at the external boundary.
[<Emit("(typeof $0 === 'number' ? $0 : typeof $0 === 'bigint' ? Number($0) : ($0 && typeof $0 === 'object' && 'toNumber' in $0 ? (() => { try { return $0.toNumber(); } catch { return undefined; } })() : undefined))")>]
let private toInt (value: obj option) : float option = jsNative

let private toSeconds (value: obj option) : float<seconds> option =
    toInt value |> Option.map LanguagePrimitives.FloatWithMeasure<seconds>

let private (|EventTime|_|) (ev: IStopTimeEvent option) : float<seconds> option =
    ev
    |> Option.bind (fun e -> toSeconds e.time)
    |> Option.filter ((<>) 0.0<seconds>)

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

let private spineStopSet = Set.ofArray PREFERRED_STOP_ORDER

let private spineIndexByStop =
    PREFERRED_STOP_ORDER |> Array.mapi (fun i id -> id, i) |> Map.ofArray

let stopSpineIndex (stopId: string) : int option = Map.tryFind stopId spineIndexByStop

// Blend linear progress with smoothstep so trains accelerate out of stations and brake into platforms
let private smoothProgress (rawP: float) : float =
    let t = clamp 0.0 1.0 rawP
    0.58 * t + 0.42 * (t * t * (3.0 - 2.0 * t))

// Snap wall clock into the feed's clock frame if MTA has a DST/timezone offset (> 25 min skew)
let effectiveNowSec (headerTimestampMs: float<milliseconds> option) : float<seconds> =
    let wallSec = nowMilliseconds () |> millisecondsToSeconds

    match headerTimestampMs with
    | Some ms when ms > 0.0<milliseconds> ->
        let diff = millisecondsToSeconds ms - wallSec

        if abs diff > 1500.0<seconds> then
            wallSec + Math.Round(diff / 1800.0<seconds>) * 1800.0<seconds>
        else
            wallSec
    | _ -> wallSec

// Extract active train trajectories along the G line, fusing TripUpdate and VehiclePosition entities
let extractRawTrips (feed: IFeedMessage) : RawTrainTrip array =
    let headerMs =
        feed.header
        |> Option.bind (fun h -> toSeconds h.timestamp)
        |> Option.filter ((<>) 0.0<seconds>)
        |> Option.map secondsToMilliseconds

    let nowSec = effectiveNowSec headerMs

    let vehiclesByTrip =
        [| for entity in feed.entity do
               match entity.vehicle with
               | Some v ->
                   match v.trip |> Option.bind _.tripId with
                   | Some tid when tid <> "" ->
                       let rawStopOpt = v.stopId |> Option.filter ((<>) "")
                       let tsOpt = toSeconds v.timestamp |> Option.filter ((<>) 0.0<seconds>)
                       let seqOpt = toInt v.currentStopSequence |> Option.map int
                       tid, (rawStopOpt, tsOpt, seqOpt)
                   | _ -> ()
               | None -> () |]
        |> Map.ofArray

    [| for idx, entity in Array.indexed feed.entity do
           match entity.tripUpdate with
           | None -> ()
           | Some tu ->
               let route = tu.trip |> Option.bind _.routeId |? "G"
               let tripId = (tu.trip |> Option.bind _.tripId) <|> entity.id |? $"{route}-{idx}"

               let baseDir =
                   match tu.trip |> Option.bind (fun t -> toInt t.directionId) with
                   | Some 0.0 -> Northbound
                   | Some 1.0 -> Southbound
                   | _ -> Other

               let rawVehStopOpt, vehTsOpt, vehSeqOpt =
                   match Map.tryFind tripId vehiclesByTrip with
                   | Some(rawStop, ts, seq) -> rawStop, ts, seq
                   | None -> None, None, None

               let dirFromId (s: string) =
                   if s.EndsWith("N") then Some Northbound
                   elif s.EndsWith("S") then Some Southbound
                   else None

               let spineStus =
                   [| for stu in (tu.stopTimeUpdate |? [||]) do
                          match stu.stopId with
                          | Some rawId when rawId <> "" ->
                              let cleanId = STOP_SUFFIX_RE.Replace(rawId, "")

                              if Set.contains cleanId spineStopSet then
                                  rawId, cleanId, stu
                          | _ -> () |]

               let stuCalls =
                   [| for _, cleanId, stu in spineStus do
                          let arrOpt =
                              match stu.arrival with
                              | EventTime t -> Some t
                              | _ -> None

                          let depOpt =
                              match stu.departure with
                              | EventTime t -> Some t
                              | _ -> None

                          match arrOpt <|> depOpt with
                          | Some primarySec when primarySec >= nowSec - 300.0<seconds> ->
                              let arrSec = arrOpt |? primarySec
                              let depSec = depOpt |? arrSec

                              { stopId = cleanId
                                arriveSec = arrSec
                                departSec = max arrSec depSec }
                          | _ -> () |]
                   |> Array.sortBy _.arriveSec

               let dir =
                   (spineStus
                    |> Array.choose (fun (rawId, _, _) -> dirFromId rawId)
                    |> Array.tryLast)
                   <|> (if tripId.Contains("..N") then Some Northbound
                        elif tripId.Contains("..S") then Some Southbound
                        else rawVehStopOpt |> Option.bind dirFromId)
                   |? baseDir

               // Prepend upstream VehiclePosition stop when MTA has already dropped it from stopTimeUpdate
               let calls =
                   match rawVehStopOpt |> Option.map (fun s -> STOP_SUFFIX_RE.Replace(s, "")), vehTsOpt with
                   | Some vStop, Some vTs when
                       stuCalls.Length > 0
                       && vTs >= nowSec - 300.0<seconds>
                       && vTs <= nowSec + 5.0<seconds>
                       && vTs < stuCalls[0].arriveSec
                       && stuCalls |> Array.forall (fun c -> c.stopId <> vStop)
                       ->
                       let vIdx = spineIndexByStop |> Map.tryFind vStop |? -1
                       let firstIdx = spineIndexByStop |> Map.tryFind stuCalls[0].stopId |? -1

                       let isUpstream =
                           match dir with
                           | Northbound -> vIdx >= 0 && firstIdx - vIdx = 1
                           | Southbound -> vIdx >= 0 && vIdx - firstIdx = 1
                           | Other -> vIdx >= 0 && Math.Abs(firstIdx - vIdx) = 1

                       if isUpstream then
                           Array.append
                               [| { stopId = vStop
                                    arriveSec = vTs
                                    departSec = vTs } |]
                               stuCalls
                       else
                           stuCalls
                   | _ -> stuCalls

               let isUnstarted =
                   (vehSeqOpt = Some 0
                    && calls.Length > 0
                    && calls[0].arriveSec > nowSec + 45.0<seconds>)
                   || (calls.Length >= 2
                       && calls[0].arriveSec > nowSec + 60.0<seconds>
                       && ((dir = Southbound && calls[0].stopId = "G22")
                           || (dir = Northbound && calls[0].stopId = "F27")))

               if
                   calls.Length > 0
                   && not isUnstarted
                   && (calls |> Array.exists (fun c -> c.departSec >= nowSec - 10.0<seconds>))
               then
                   { tripId = tripId
                     direction = dir
                     calls = calls } |]
    |> Array.sortBy _.tripId

// Interpolate a continuous index (0.0 .. 20.0) along PREFERRED_STOP_ORDER into (latitude, longitude, headingDeg)
let private poseAtSpinePos
    (stopLookup: Map<string, StopOption>)
    (direction: Direction)
    (spinePos: float)
    : (float<latitude> * float<longitude> * float) option =
    let maxIdx = PREFERRED_STOP_ORDER.Length - 1
    let clamped = clamp 0.0 (float maxIdx) spinePos
    let i0 = int (Math.Floor(clamped))
    let i1 = min maxIdx (i0 + 1)

    match Map.tryFind PREFERRED_STOP_ORDER[i0] stopLookup, Map.tryFind PREFERRED_STOP_ORDER[i1] stopLookup with
    | Some { latitude = Some lat0
             longitude = Some lon0 },
      Some { latitude = Some lat1
             longitude = Some lon1 } ->
        let s0 = projectOntoTrack (projectToMapPx lat0 lon0)
        let s1 = projectOntoTrack (projectToMapPx lat1 lon1)
        let s = s0 + (s1 - s0) * (clamped - float i0)
        let px, py = pointAtArc s
        let lat, lon = mapPxToLatLon px py
        let pxN, pyN = pointAtArc (s - 2.0)
        let pxS, pyS = pointAtArc (s + 2.0)

        let vx, vy =
            if direction = Southbound then
                pxS - pxN, pyS - pyN
            else
                pxN - pxS, pyN - pyS

        let heading =
            if Math.Abs(vx) < 1e-6 && Math.Abs(vy) < 1e-6 then
                (if direction = Southbound then 180.0 else 0.0)
            else
                Math.Atan2(vx, -vy) * 180.0 / Math.PI

        Some(lat, lon, heading)
    | _ -> None

// Estimate each active train's live (latitude, longitude, heading) along the G-line track spine
// with monotonic, velocity-bounded smoothing so markers never jump backwards or teleport ahead.
let estimateTrainPositions
    (nowSec: float<seconds>)
    (stopLookup: Map<string, StopOption>)
    (prevPositions: Map<string, float * float<seconds>>)
    (trips: RawTrainTrip array)
    : EstimatedTrain array * Map<string, float * float<seconds>> =

    let stepped =
        trips
        |> Array.choose (fun trip ->
            let distinctCalls =
                trip.calls
                |> Array.fold
                    (fun (acc: TrainStopCall list) c ->
                        match acc with
                        | prev :: _ when prev.stopId = c.stopId -> acc
                        | _ -> c :: acc)
                    []
                |> List.rev
                |> List.toArray

            // Account for ~15s station dwell time when MTA reports identical arrival & departure timestamps
            let effectiveDepart (idx: int) (c: TrainStopCall) =
                if c.departSec > c.arriveSec + 1.0<seconds> then
                    c.departSec
                elif idx + 1 < distinctCalls.Length then
                    c.departSec
                    + clamp
                        0.0<seconds>
                        15.0<seconds>
                        ((distinctCalls[idx + 1].arriveSec - c.departSec - 30.0<seconds>) * 0.25)
                else
                    c.departSec + 10.0<seconds>

            distinctCalls
            |> Array.tryFindIndex (fun c -> c.departSec >= nowSec - 5.0<seconds>)
            |> Option.bind (fun nextIdx ->
                let nextCall = distinctCalls[nextIdx]
                let nextSpineIdx = spineIndexByStop |> Map.tryFind nextCall.stopId |? 0
                let remSec = nextCall.arriveSec - nowSec

                // PREFERRED_STOP_ORDER runs South (F27 = 0) to North (G22 = 20)
                let targetSpinePosOpt =
                    if remSec <= 8.0<seconds> then
                        Some(float nextSpineIdx)
                    elif nextIdx > 0 then
                        let prevCall = distinctCalls[nextIdx - 1]
                        let prevSpineIdx = spineIndexByStop |> Map.tryFind prevCall.stopId |? nextSpineIdx
                        let effDepSec = effectiveDepart (nextIdx - 1) prevCall

                        if nowSec <= effDepSec then
                            Some(float prevSpineIdx)
                        else
                            let p =
                                clamp
                                    0.02
                                    0.98
                                    (smoothProgress (
                                        (nowSec - effDepSec) / max 25.0<seconds> (nextCall.arriveSec - effDepSec)
                                    ))

                            Some(float prevSpineIdx + (float nextSpineIdx - float prevSpineIdx) * p)
                    else
                        let upstreamSpineIdx =
                            match trip.direction with
                            | Northbound -> max 0 (nextSpineIdx - 1)
                            | Southbound -> min (PREFERRED_STOP_ORDER.Length - 1) (nextSpineIdx + 1)
                            | Other -> nextSpineIdx

                        if upstreamSpineIdx <> nextSpineIdx && remSec <= 150.0<seconds> then
                            let p = clamp 0.03 0.98 (smoothProgress (1.0 - remSec / max remSec 95.0<seconds>))
                            Some(float upstreamSpineIdx + (float nextSpineIdx - float upstreamSpineIdx) * p)
                        elif upstreamSpineIdx = nextSpineIdx then
                            Some(float nextSpineIdx)
                        else
                            None

                targetSpinePosOpt
                |> Option.map (fun targetSpinePos ->
                    let spinePos =
                        match Map.tryFind trip.tripId prevPositions with
                        | None -> targetSpinePos
                        | Some(prevSpinePos, prevTimeSec) ->
                            let dt = clamp 0.0<seconds> 60.0<seconds> (nowSec - prevTimeSec)
                            let dirSign = if trip.direction = Southbound then -1.0 else 1.0
                            let signedDelta = (targetSpinePos - prevSpinePos) * dirSign

                            if signedDelta < -1.8 || signedDelta > 2.5 then
                                targetSpinePos
                            elif signedDelta <= 0.0 then
                                prevSpinePos
                            else
                                let maxStep =
                                    dt
                                    * min 0.032<1 / seconds> (0.013<1 / seconds> + 0.025<1 / seconds> * signedDelta)

                                prevSpinePos + dirSign * min signedDelta maxStep

                    let isStopped = Math.Abs(spinePos - Math.Round(spinePos)) < 0.02

                    let trainOpt =
                        poseAtSpinePos stopLookup trip.direction spinePos
                        |> Option.map (fun (lat, lon, headingDeg) ->
                            { tripId = trip.tripId
                              direction = trip.direction
                              latitude = lat
                              longitude = lon
                              spinePos = spinePos
                              headingDeg = headingDeg
                              isStopped = isStopped
                              nextStopId = nextCall.stopId })

                    (trip.tripId, (spinePos, nowSec)), trainOpt)))

    stepped |> Array.choose snd, stepped |> Array.map fst |> Map.ofArray

let private formatHoursMinutes (m: int) (suffix: string) =
    match m / 60, m % 60 with
    | h, 0 -> $"{h}h{suffix}"
    | h, min -> $"{h}h {min}m{suffix}"

let formatRelative (target: DateTimeOffset) : string =
    let remaining = target - DateTimeOffset.UtcNow

    if remaining.TotalSeconds <= 0.0 then
        "due"
    elif remaining.TotalMinutes < 1.0 then
        "<1 min"
    else
        match int (Math.Floor remaining.TotalMinutes) with
        | m when m < 60 -> $"in {m} min"
        | m -> "in " + formatHoursMinutes m ""

let formatAge (target: DateTimeOffset) : string =
    match roundInt (DateTimeOffset.UtcNow - target).TotalSeconds with
    | s when s <= 0 -> "just now"
    | s when s < 60 -> $"{s}s ago"
    | s ->
        match roundInt (float s / 60.0) with
        | 1 -> "1 min ago"
        | m when m < 60 -> $"{m} min ago"
        | m -> formatHoursMinutes m " ago"

let headway (current: Arrival) (next: Arrival option) : string =
    match next with
    | Some n ->
        match roundInt (n.time - current.time).TotalMinutes with
        | m when m > 0 -> $" • next in {m}m"
        | _ -> ""
    | None -> ""

let parseFeed (feed: IFeedMessage) (stopIdPrefix: string) : ArrivalGroup * FeedMeta =
    let currentNow = DateTimeOffset.UtcNow

    let headerTimestampMs =
        feed.header
        |> Option.bind (fun h -> toSeconds h.timestamp)
        |> Option.filter ((<>) 0.0<seconds>)
        |> Option.map secondsToMilliseconds

    let allArrivals =
        [| for idx, entity in Array.indexed feed.entity do
               match entity.tripUpdate with
               | None -> ()
               | Some tu ->
                   let route = tu.trip |> Option.bind _.routeId |? "?"
                   let tripId = (tu.trip |> Option.bind _.tripId) <|> entity.id |? $"{route}-{idx}"

                   let baseDir =
                       match tu.trip |> Option.bind (fun t -> toInt t.directionId) with
                       | Some 0.0 -> Northbound
                       | Some 1.0 -> Southbound
                       | _ -> Other

                   for stu in (tu.stopTimeUpdate |? [||]) do
                       match stu.stopId with
                       | Some stopId when stopId.StartsWith(stopIdPrefix) ->
                           match stu.arrival, stu.departure with
                           | EventTime ts, _
                           | _, EventTime ts ->
                               let whenDate = instantFromSeconds ts

                               if whenDate >= currentNow then
                                   let dir =
                                       if stopId.EndsWith("N") then Northbound
                                       elif stopId.EndsWith("S") then Southbound
                                       else baseDir

                                   { time = whenDate
                                     route = route
                                     direction = dir
                                     tripId = Some tripId
                                     label = None }
                           | _ -> ()
                       | _ -> () |]
        |> Array.sortBy _.time

    let byDir d =
        allArrivals |> Array.filter (fun a -> a.direction = d)

    { Northbound = byDir Northbound
      Southbound = byDir Southbound
      Other = byDir Other },
    { headerTimestampMs = headerTimestampMs }
