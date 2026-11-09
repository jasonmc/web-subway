module Ui

open System
open Fable.Core
open Config
open State
open Stops
open Feed

type IDomElement =
    abstract className: string with get, set
    abstract textContent: string with get, set
    abstract innerHTML: string with get, set
    abstract value: string with get, set
    abstract addEventListener: string * (obj -> unit) -> unit

[<Emit("document.querySelector($0)")>]
let private querySelector (selector: string) : IDomElement option = jsNative

let private requiredElement (selector: string) : IDomElement =
    querySelector selector
    |> Option.defaultWith (fun () -> failwith $"Missing required UI element: {selector}")

let private statusEl = requiredElement "#status"
let private listsEl = requiredElement "#lists"
let private stopSelect = requiredElement "#stop-select"
let private stopNameEl = requiredElement "#stop-name"
let private mapElOpt = querySelector "#map"

let private lastRenderedStopsKey = ref ""
let private lastSyncedStop = ref ""
let private lastRenderedListsHtml = ref ""
let private lastRenderedMapStopsHtml = ref ""
let private lastRenderedTrainsHtml = ref ""
let private spotlightTripId : string option ref = ref None

[<Emit("""(() => {
  const tripId = $0;
  if (typeof document.querySelectorAll !== 'function') return;
  for (const el of document.querySelectorAll('[data-trip-id]') || []) {
    el.classList.toggle('is-spotlight', Boolean(tripId && el.getAttribute('data-trip-id') === tripId));
  }
})()""")>]
let private syncSpotlightDom (tripIdOpt: string option) : unit = jsNative

[<Emit("($0 && $0.target && $0.target.closest ? ($0.target.closest('[' + $1 + ']')?.getAttribute($1) ?? null) : null)")>]
let private closestAttr (ev: obj) (attr: string) : string option = jsNative

[<Emit("(() => { try { const u = new URL(window.location.href); u.hash = $0; window.history.replaceState({}, '', u); } catch {} })()")>]
let private syncStopParam (stopPrefix: string) : unit = jsNative

[<Emit("$0.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })")>]
let private formatLocaleTime (d: JS.Date) : string = jsNative

let setStatusText (text: string) (isError: bool) =
    statusEl.className <- if isError then "status error" else "status"
    statusEl.textContent <- text

let setError (message: string) =
    setStatusText message true
    listsEl.innerHTML <- ""
    lastRenderedListsHtml.Value <- ""

let private describeTrainForStop (model: Model) (train: EstimatedTrain) : string =
    if train.nextStopId = model.selectedStopPrefix then
        if train.isStopped then "At station now"
        else "Next stop · approaching station"
    else
        let nextName = stopLabel model.stopLookup train.nextStopId
        let selIdx = stopSpineIndex model.selectedStopPrefix |? 0
        let nextIdx = stopSpineIndex train.nextStopId |? selIdx
        let stopsAway = max 1 (Math.Abs(selIdx - nextIdx) + (if train.isStopped then 0 else 1))
        let stopsText = if stopsAway = 1 then "1 stop away" else $"{stopsAway} stops away"
        let actionText = if train.isStopped then $"At {nextName}" else $"Approaching {nextName}"
        $"{actionText} · {stopsText}"

let private renderArrivals (model: Model) (arrivals: ArrivalGroup) =
    let trainByTrip =
        model.mapTrains
        |> Array.map (fun t -> t.tripId, t)
        |> Map.ofArray

    let cards = [|
        "Northbound", "⬆️", arrivals.Northbound
        "Southbound", "⬇️", arrivals.Southbound
        "Other", "⬜", arrivals.Other
    |]

    let html =
        cards
        |> Array.choose (fun (title, icon, items) ->
            match Array.truncate ARRIVALS_PER_DIRECTION items with
            | [||] -> None
            | visible ->
                let rows =
                    visible
                    |> Array.mapi (fun i a ->
                        let rel = formatRelative a.time
                        let hw = headway a (Array.tryItem (i + 1) visible)
                        let t = formatLocaleTime a.time
                        let matchedTrain = a.tripId |> Option.bind (fun tid -> Map.tryFind tid trainByTrip)
                        let tripAttr =
                            match a.tripId with
                            | Some tid -> $" data-trip-id=\"{tid}\""
                            | None -> ""
                        let spotlightCls =
                            match a.tripId, spotlightTripId.Value with
                            | Some tid, Some s when tid = s -> " is-spotlight"
                            | _ -> ""
                        let nextCls = if i = 0 && Option.isSome matchedTrain then " arrival-next" else ""
                        let whereHtml =
                            match matchedTrain with
                            | Some tr ->
                                let desc = describeTrainForStop model tr
                                $"<div class=\"arrival-where\">{desc}</div>"
                            | None -> ""
                        $"<div class=\"arrival{nextCls}{spotlightCls}\"{tripAttr}><div class=\"arrival-main\"><div><span class=\"route\">{a.route}</span><span class=\"meta\"> {rel}{hw}</span></div>{whereHtml}</div><span class=\"time\">{t}</span></div>")
                    |> String.concat ""
                Some $"<article class=\"card\"><h2>{icon} {title}</h2>{rows}</article>")
        |> String.concat ""

    let nextHtml =
        if String.IsNullOrWhiteSpace(html) then
            "<article class=\"card\"><p class=\"meta\">No arrivals in the feed.</p></article>"
        else
            html

    if nextHtml <> lastRenderedListsHtml.Value then
        listsEl.innerHTML <- nextHtml
        lastRenderedListsHtml.Value <- nextHtml

let private buildUserLocationHtml (model: Model) : string =
    match model.userLocation with
    | Some (lat, lon) ->
        let px, py = projectToMapPx lat lon
        if px >= 0.0 && px <= MAP_WIDTH && py >= 0.0 && py <= MAP_HEIGHT then
            let leftPct, topPct = pxToMapPercent px py
            $"<div class=\"osm-user-location\" data-lat=\"{lat}\" data-lon=\"{lon}\" title=\"Your location\" aria-label=\"Your location\" style=\"left:{leftPct};top:{topPct}\"><span class=\"osm-user-pulse\"></span><span class=\"osm-user-dot\"></span></div>"
        else
            ""
    | None -> ""

let private buildStationsHtml (model: Model) : string =
    let stopsHtml =
        PREFERRED_STOP_ORDER
        |> Array.choose (fun id ->
            match Map.tryFind id model.stopLookup with
            | Some { name = name; latitude = Some lat; longitude = Some lon } ->
                let leftPct, topPct = projectToMapPercent lat lon
                let isSelected = id = model.selectedStopPrefix
                let cls = if isSelected then "osm-stop osm-stop-selected" else "osm-stop"
                let labelHtml =
                    if isSelected then $"<span class=\"osm-stop-label\">{name}</span>"
                    else ""
                Some $"<button type=\"button\" class=\"{cls}\" data-stop-id=\"{id}\" title=\"{name}\" style=\"left:{leftPct};top:{topPct}\">{labelHtml}</button>"
            | _ -> None)
        |> String.concat ""
    stopsHtml + buildUserLocationHtml model

let private buildTrainsHtml (model: Model) : string =
    let nextIncomingTrips =
        match model.lastArrivals with
        | None -> Set.empty
        | Some arr ->
            [ arr.Northbound |> Array.tryHead |> Option.bind _.tripId
              arr.Southbound |> Array.tryHead |> Option.bind _.tripId ]
            |> List.choose id
            |> Set.ofList

    let allIncomingTrips =
        match model.lastArrivals with
        | None -> Set.empty
        | Some arr ->
            Array.concat [ arr.Northbound; arr.Southbound; arr.Other ]
            |> Array.choose _.tripId
            |> Set.ofArray

    let selIdx = float (stopSpineIndex model.selectedStopPrefix |? 0)

    model.mapTrains
    |> Array.map (fun t ->
        let px, py = projectToMapPx t.latitude t.longitude
        let minSpineGap =
            model.mapTrains
            |> Array.fold (fun acc o -> if o.tripId = t.tripId then acc else min acc (Math.Abs(o.spinePos - t.spinePos))) 99.0
        let rad = t.headingDeg * Math.PI / 180.0
        let offsetDist = 7.5 * max 0.0 (min 1.0 ((0.75 - minSpineGap) / 0.5))
        let leftPct, topPct = pxToMapPercent (px + Math.Cos(rad) * offsetDist) (py + Math.Sin(rad) * offsetDist)
        let dirLabel = match t.direction with Northbound -> "Northbound" | Southbound -> "Southbound" | Other -> "Train"
        let nextName = stopLabel model.stopLookup t.nextStopId
        let statusLabel = if t.isStopped then $"At {nextName}" else $"{dirLabel} → {nextName}"
        let headingStr = toFixed t.headingDeg 1
        let isNext = Set.contains t.tripId nextIncomingTrips
        let isPassed =
            not (Set.contains t.tripId allIncomingTrips)
            && ((t.direction = Northbound && t.spinePos > selIdx + 0.05)
                || (t.direction = Southbound && t.spinePos < selIdx - 0.05))
        let cls =
            "osm-train"
            + (if isNext then " osm-train-next" elif isPassed then " osm-train-passed" else "")
            + (if spotlightTripId.Value = Some t.tripId then " is-spotlight" else "")
        let svgHtml =
            $"<svg class=\"osm-train-svg\" viewBox=\"0 0 28 28\" aria-hidden=\"true\"><g class=\"osm-train-dir\" transform=\"rotate({headingStr} 14 14)\"><path class=\"osm-train-badge\" d=\"M14 1.2 L19.2 7.8 A9.2 9.2 0 1 1 8.8 7.8 Z\"/></g><rect class=\"osm-train-car\" x=\"9.5\" y=\"9.2\" width=\"9\" height=\"9.2\" rx=\"2.2\"/><line class=\"osm-train-line\" x1=\"9.5\" y1=\"14.2\" x2=\"18.5\" y2=\"14.2\"/><circle class=\"osm-train-light\" cx=\"11.8\" cy=\"16.4\" r=\"0.95\"/><circle class=\"osm-train-light\" cx=\"16.2\" cy=\"16.4\" r=\"0.95\"/><line class=\"osm-train-rail\" x1=\"11.2\" y1=\"18.4\" x2=\"10.2\" y2=\"20.2\"/><line class=\"osm-train-rail\" x1=\"16.8\" y1=\"18.4\" x2=\"17.8\" y2=\"20.2\"/></svg>"
        $"<div class=\"{cls}\" data-trip-id=\"{t.tripId}\" data-lat=\"{t.latitude}\" data-lon=\"{t.longitude}\" data-heading=\"{headingStr}\" title=\"{statusLabel}\" style=\"left:{leftPct};top:{topPct}\">{svgHtml}</div>")
    |> String.concat ""

let private renderMap (model: Model) =
    match mapElOpt with
    | None -> ()
    | Some mapEl ->
        let stationsHtml = buildStationsHtml model
        let trainsHtml = buildTrainsHtml model
        match querySelector "#osm-stops", querySelector "#osm-trains" with
        | Some stopsEl, Some trainsEl ->
            if stationsHtml <> lastRenderedMapStopsHtml.Value then
                stopsEl.innerHTML <- stationsHtml
                lastRenderedMapStopsHtml.Value <- stationsHtml
            if trainsHtml <> lastRenderedTrainsHtml.Value then
                trainsEl.innerHTML <- trainsHtml
                lastRenderedTrainsHtml.Value <- trainsHtml
        | _ ->
            if stationsHtml <> lastRenderedMapStopsHtml.Value || trainsHtml <> lastRenderedTrainsHtml.Value then
                mapEl.innerHTML <-
                    $"<article class=\"card map-card\"><div class=\"osm-viewport\"><div id=\"osm-tiles\" class=\"osm-tiles\">{STATIC_BASE_SVG}</div><div id=\"osm-stops\" class=\"osm-overlay\">{stationsHtml}</div><div id=\"osm-trains\" class=\"osm-overlay osm-trains-layer\">{trainsHtml}</div><div class=\"osm-attrib\">© <a href=\"https://www.openstreetmap.org/copyright\" target=\"_blank\" rel=\"noopener noreferrer\">OSM</a> · <a href=\"https://github.com/chriswhong/nyc-neighborhood-boundaries\" target=\"_blank\" rel=\"noopener noreferrer\">Whong</a></div></div></article>"
                lastRenderedMapStopsHtml.Value <- stationsHtml
                lastRenderedTrainsHtml.Value <- trainsHtml

let private renderStopSelect (model: Model) =
    let available = model.availableStops |> Option.filter (not << Set.isEmpty)
    let matchingIds =
        PREFERRED_STOP_ORDER
        |> Array.filter (fun id -> available |> Option.forall (Set.contains id))

    let finalIds = if matchingIds.Length > 0 then matchingIds else PREFERRED_STOP_ORDER
    let preservedIds =
        if Array.contains model.selectedStopPrefix finalIds then finalIds
        else Array.append [| model.selectedStopPrefix |] finalIds

    let nextKey = String.concat "," preservedIds
    if nextKey <> lastRenderedStopsKey.Value then
        stopSelect.innerHTML <-
            preservedIds
            |> Array.map (fun id ->
                let name = model.stopLookup |> Map.tryFind id |> Option.map _.name |? id
                let sel = if id = model.selectedStopPrefix then "selected" else ""
                $"<option value=\"{id}\" {sel}>{name}</option>")
            |> String.concat ""
        lastRenderedStopsKey.Value <- nextKey

    stopSelect.value <- model.selectedStopPrefix
    if model.selectedStopPrefix <> lastSyncedStop.Value then
        stopNameEl.textContent <- stopLabel model.stopLookup model.selectedStopPrefix
        syncStopParam model.selectedStopPrefix
        lastSyncedStop.Value <- model.selectedStopPrefix

// Elmish View function
let view (model: Model) =
    renderStopSelect model
    match model.status with
    | Loading -> setStatusText "Loading…" false
    | ErrorMessage msg -> setStatusText msg true
    | FeedAge tsOpt ->
        let age =
            match tsOpt with
            | Some ms when ms <> 0.0 -> formatAge (createDateMs ms)
            | _ -> "unknown"
        setStatusText $"Feed age: {age}" false

    Option.iter (renderArrivals model) model.lastArrivals
    if Option.isSome model.lastArrivals then
        renderMap model

let private setSpotlight (tripIdOpt: string option) =
    if spotlightTripId.Value <> tripIdOpt then
        spotlightTripId.Value <- tripIdOpt
        syncSpotlightDom tripIdOpt

let private toggleSpotlight (ev: obj) =
    match closestAttr ev "data-trip-id" with
    | Some tid when spotlightTripId.Value = Some tid -> setSpotlight None
    | Some tid -> setSpotlight (Some tid)
    | None -> ()

let private bindSpotlight (el: IDomElement) =
    el.addEventListener("mouseover", fun ev -> closestAttr ev "data-trip-id" |> setSpotlight)
    el.addEventListener("mouseout", fun _ -> setSpotlight None)

let bindStopSelect (dispatch: Dispatch<Msg>) =
    stopSelect.addEventListener("change", fun _ ->
        dispatch (SelectStop stopSelect.value))
    bindSpotlight listsEl
    listsEl.addEventListener("click", toggleSpotlight)
    mapElOpt |> Option.iter (fun mapEl ->
        bindSpotlight mapEl
        mapEl.addEventListener("click", fun ev ->
            match closestAttr ev "data-stop-id" with
            | Some stopId -> dispatch (SelectStop stopId)
            | None -> toggleSpotlight ev))
