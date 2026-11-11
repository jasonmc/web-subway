module Ui

open System
open Browser
open Browser.Types
open Feliz
open Feliz.Bulma
open Elmish
open Config
open State
open Stops
open Feed

let syncStopParam (stopPrefix: string) : unit =
    try window.history.replaceState(null, "", $"#{stopPrefix}")
    with _ -> ()

let setError (message: string) : unit =
    match document.querySelector "#status" with
    | null -> ()
    | el ->
        el.textContent <- message
        el.className <- "notification is-danger is-light status error"

[<Fable.Core.Import("flushSync", "react-dom")>]
let private flushSync (fn: unit -> unit) : unit = Fable.Core.Util.jsNative

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

let private renderArrivals (model: Model) (dispatch: Dispatch<Msg>) (arrivals: ArrivalGroup) : ReactElement list =
    let trainByTrip =
        model.mapTrains
        |> Array.map (fun t -> t.tripId, t)
        |> Map.ofArray

    let cards = [
        "Northbound", "M12 20 V4 M5 11 L12 4 L19 11", arrivals.Northbound
        "Southbound", "M12 4 V20 M5 13 L12 20 L19 13", arrivals.Southbound
        "Other", "M5 5 H19 V19 H5 Z", arrivals.Other
    ]

    let renderedCards =
        cards
        |> List.choose (fun (title, icon, items) ->
            match Array.truncate ARRIVALS_PER_DIRECTION items with
            | [||] -> None
            | visible ->
                let rows =
                    visible
                    |> Array.mapi (fun i a ->
                        let rel = formatRelative a.time
                        let hw = headway a (Array.tryItem (i + 1) visible)
                        let t = a.time.ToString("h:mm tt")
                        let matchedTrain = a.tripId |> Option.bind (fun tid -> Map.tryFind tid trainByTrip)
                        let isSpotlight =
                            match a.tripId, model.spotlightTripId with
                            | Some tid, Some s when tid = s -> true
                            | _ -> false
                        let isNext = i = 0 && Option.isSome matchedTrain
                        let cls =
                            "arrival"
                            + (if isNext then " arrival-next" else "")
                            + (if isSpotlight then " is-spotlight" else "")
                        Html.div [
                            prop.key (a.tripId |? $"{title}-{i}")
                            prop.className cls
                            match a.tripId with
                            | Some tid ->
                                prop.custom ("data-trip-id", tid)
                                prop.onMouseOver (fun _ -> dispatch (SetSpotlight (Some tid)))
                                prop.onMouseOut (fun _ -> dispatch (SetSpotlight None))
                                prop.onClick (fun _ -> dispatch (ToggleSpotlight tid))
                            | None -> ()
                            prop.children [
                                Html.div [
                                    prop.className "arrival-main"
                                    prop.children [
                                        Html.div [
                                            Html.span [ prop.className "route"; prop.text a.route ]
                                            Html.span [ prop.className "meta"; prop.text $" {rel}{hw}" ]
                                        ]
                                        match matchedTrain with
                                        | Some tr ->
                                            Html.div [
                                                prop.className "arrival-where"
                                                prop.text (describeTrainForStop model tr)
                                            ]
                                        | None -> ()
                                    ]
                                ]
                                Html.span [ prop.className "time"; prop.text t ]
                            ]
                        ])
                Some (
                    Bulma.card [
                        prop.key title
                        prop.children [
                            Bulma.cardContent [
                                prop.children [
                                    Bulma.title.h2 [
                                        Feliz.Bulma.title.is5
                                        prop.className "mb-3"
                                        prop.children [
                                            Svg.svg [
                                                svg.className "direction-icon"
                                                svg.viewBox (0, 0, 24, 24)
                                                svg.custom ("aria-hidden", "true")
                                                svg.children [
                                                    Svg.path [
                                                        svg.d icon
                                                        svg.fill "none"
                                                        svg.stroke "currentColor"
                                                        svg.strokeWidth 2
                                                        svg.strokeLineCap "round"
                                                        svg.strokeLineJoin "round"
                                                    ]
                                                ]
                                            ]
                                            Html.text title
                                        ]
                                    ]
                                    yield! rows
                                ]
                            ]
                        ]
                    ]
                ))

    if List.isEmpty renderedCards then
        [ Bulma.card [
              Bulma.cardContent [
                  Html.p [ prop.className "meta"; prop.text "No arrivals in the feed." ]
              ]
          ] ]
    else
        renderedCards

let private renderStations (model: Model) (dispatch: Dispatch<Msg>) : ReactElement list =
    let stops =
        PREFERRED_STOP_ORDER
        |> Array.choose (fun id ->
            match Map.tryFind id model.stopLookup with
            | Some { name = name; latitude = Some lat; longitude = Some lon } ->
                let leftPct, topPct = projectToMapPercent lat lon
                let isSelected = id = model.selectedStopPrefix
                Some (
                    Html.button [
                        prop.key id
                        prop.type'.button
                        prop.className (if isSelected then "osm-stop osm-stop-selected" else "osm-stop")
                        prop.custom ("data-stop-id", id)
                        prop.title name
                        prop.style [ style.custom ("left", leftPct); style.custom ("top", topPct) ]
                        prop.onClick (fun _ -> dispatch (SelectStop id))
                        prop.children [
                            if isSelected then
                                Html.span [ prop.className "osm-stop-label"; prop.text name ]
                        ]
                    ]
                )
            | _ -> None)
        |> Array.toList

    let userLoc =
        match model.userLocation with
        | Some (lat, lon) ->
            let px, py = projectToMapPx lat lon
            if px >= 0.0 && px <= MAP_WIDTH && py >= 0.0 && py <= MAP_HEIGHT then
                let leftPct, topPct = pxToMapPercent px py
                [ Html.div [
                      prop.key "user-location"
                      prop.className "osm-user-location"
                      prop.custom ("data-lat", string lat)
                      prop.custom ("data-lon", string lon)
                      prop.title "Your location"
                      prop.ariaLabel "Your location"
                      prop.style [ style.custom ("left", leftPct); style.custom ("top", topPct) ]
                      prop.children [
                          Html.span [ prop.className "osm-user-pulse" ]
                          Html.span [ prop.className "osm-user-dot" ]
                      ]
                  ] ]
            else []
        | None -> []

    stops @ userLoc

let private renderTrains (model: Model) (dispatch: Dispatch<Msg>) : ReactElement list =
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
            + (if model.spotlightTripId = Some t.tripId then " is-spotlight" else "")
        Html.div [
            prop.key t.tripId
            prop.className cls
            prop.custom ("data-trip-id", t.tripId)
            prop.custom ("data-lat", string t.latitude)
            prop.custom ("data-lon", string t.longitude)
            prop.custom ("data-heading", headingStr)
            prop.title statusLabel
            prop.style [ style.custom ("left", leftPct); style.custom ("top", topPct) ]
            prop.onMouseOver (fun _ -> dispatch (SetSpotlight (Some t.tripId)))
            prop.onMouseOut (fun _ -> dispatch (SetSpotlight None))
            prop.onClick (fun _ -> dispatch (ToggleSpotlight t.tripId))
            prop.children [
                Svg.svg [
                    svg.className "osm-train-svg"
                    svg.viewBox (0, 0, 28, 28)
                    svg.custom ("aria-hidden", "true")
                    svg.children [
                        Svg.g [
                            svg.className "osm-train-dir"
                            svg.custom ("transform", $"rotate({headingStr} 14 14)")
                            svg.children [
                                Svg.path [ svg.className "osm-train-badge"; svg.d "M14 1.2 L19.2 7.8 A9.2 9.2 0 1 1 8.8 7.8 Z" ]
                            ]
                        ]
                        Svg.rect [ svg.className "osm-train-car"; svg.x 9.5; svg.y 9.2; svg.width 9.0; svg.height 9.2; svg.rx 2.2 ]
                        Svg.line [ svg.className "osm-train-line"; svg.x1 9.5; svg.y1 14.2; svg.x2 18.5; svg.y2 14.2 ]
                        Svg.circle [ svg.className "osm-train-light"; svg.cx 11.8; svg.cy 16.4; svg.r 0.95 ]
                        Svg.circle [ svg.className "osm-train-light"; svg.cx 16.2; svg.cy 16.4; svg.r 0.95 ]
                        Svg.line [ svg.className "osm-train-rail"; svg.x1 11.2; svg.y1 18.4; svg.x2 10.2; svg.y2 20.2 ]
                        Svg.line [ svg.className "osm-train-rail"; svg.x1 16.8; svg.y1 18.4; svg.x2 17.8; svg.y2 20.2 ]
                    ]
                ]
            ]
        ])
    |> Array.toList

let private renderMap (model: Model) (dispatch: Dispatch<Msg>) : ReactElement =
    Bulma.card [
        prop.className "map-card"
        prop.children [
            Html.div [
                prop.className "osm-viewport"
                prop.children [
                    Html.div [
                        prop.id "osm-tiles"
                        prop.className "osm-tiles"
                        prop.dangerouslySetInnerHTML STATIC_BASE_SVG
                    ]
                    Html.div [
                        prop.id "osm-stops"
                        prop.className "osm-overlay"
                        prop.children (renderStations model dispatch)
                    ]
                    Html.div [
                        prop.id "osm-trains"
                        prop.className "osm-overlay osm-trains-layer"
                        prop.children (renderTrains model dispatch)
                    ]
                    Html.div [
                        prop.className "osm-attrib"
                        prop.children [
                            Html.text "© "
                            Html.a [
                                prop.href "https://www.openstreetmap.org/copyright"
                                prop.target.blank
                                prop.rel "noopener noreferrer"
                                prop.text "OSM"
                            ]
                            Html.text " · "
                            Html.a [
                                prop.href "https://github.com/chriswhong/nyc-neighborhood-boundaries"
                                prop.target.blank
                                prop.rel "noopener noreferrer"
                                prop.text "Whong"
                            ]
                        ]
                    ]
                ]
            ]
        ]
    ]

let view (model: Model) (dispatch: Dispatch<Msg>) : ReactElement =
    let available = model.availableStops |> Option.filter (not << Set.isEmpty)
    let matchingIds =
        PREFERRED_STOP_ORDER
        |> Array.filter (fun id -> available |> Option.forall (Set.contains id))
    let finalIds = if matchingIds.Length > 0 then matchingIds else PREFERRED_STOP_ORDER
    let preservedIds =
        if Array.contains model.selectedStopPrefix finalIds then finalIds
        else Array.append [| model.selectedStopPrefix |] finalIds

    let statusText, isError =
        match model.status with
        | Loading -> "Loading…", false
        | ErrorMessage msg -> msg, true
        | FeedAge tsOpt ->
            let age =
                match tsOpt with
                | Some ms when ms <> 0.0 -> formatAge (createDateMs ms)
                | _ -> "unknown"
            $"Feed age: {age}", false

    React.Fragment [
        Html.header [
            prop.className "app-header"
            prop.children [
                Bulma.field.div [
                    prop.className "station-field mb-0"
                    prop.children [
                        Bulma.control.div [
                            Bulma.select [
                                select.isMedium
                                color.isSuccess
                                prop.id "stop-select"
                                prop.name "stop-select"
                                prop.ariaLabel "Station"
                                prop.value model.selectedStopPrefix
                                prop.onChange (fun (value: string) -> dispatch (SelectStop value))
                                prop.children [
                                    for id in preservedIds do
                                        let name = model.stopLookup |> Map.tryFind id |> Option.map _.name |? id
                                        Html.option [
                                            prop.key id
                                            prop.value id
                                            prop.text name
                                        ]
                                ]
                            ]
                        ]
                    ]
                ]
                Bulma.notification [
                    prop.id "status"
                    prop.role "status"
                    color.isLight
                    if isError then color.isDanger else color.isInfo
                    prop.className (if isError then "status error" else "status")
                    prop.text statusText
                ]
            ]
        ]
        Bulma.columns [
            prop.className "app-content"
            prop.children [
                Bulma.column [
                    prop.className "arrivals-column"
                    prop.children [
                        Html.section [
                            prop.id "lists"
                            prop.className "lists"
                            prop.children (
                                match model.lastArrivals with
                                | Some arr -> renderArrivals model dispatch arr
                                | None -> []
                            )
                        ]
                    ]
                ]
                Bulma.column [
                    prop.className "map-column"
                    prop.children [
                        Html.section [
                            prop.id "map"
                            prop.className "map-section"
                            prop.ariaLabel "Live train map"
                            prop.children [
                                if Option.isSome model.lastArrivals then
                                    renderMap model dispatch
                            ]
                        ]
                    ]
                ]
            ]
        ]
    ]

let withReactRoot (program: Program<'arg, Model, Msg, ReactElement>) : Program<'arg, Model, Msg, ReactElement> =
    let mutable root : IReactRoot option = None
    program
    |> Program.withSetState (fun model dispatch ->
        let r =
            match root with
            | Some existing -> existing
            | None ->
                let created = ReactDOM.createRoot (document.querySelector ".app" :?> HTMLElement)
                root <- Some created
                created
        flushSync (fun () -> r.render (view model dispatch)))
