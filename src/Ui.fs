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
    abstract addEventListener: string * (unit -> unit) -> unit

[<Emit("document.querySelector($0)")>]
let private querySelector (selector: string) : IDomElement option = jsNative

let private requiredElement (selector: string) : IDomElement =
    querySelector selector
    |> Option.defaultWith (fun () -> failwith $"Missing required UI element: {selector}")

let private statusEl = requiredElement "#status"
let private listsEl = requiredElement "#lists"
let private stopSelect = requiredElement "#stop-select"
let private stopNameEl = requiredElement "#stop-name"
let private lastRenderedStopsKey = ref ""
let private lastSyncedStop = ref ""
let private lastRenderedListsHtml = ref ""

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

let private renderArrivals (arrivals: ArrivalGroup) =
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
                        $"<div class=\"arrival\"><div><span class=\"route\">{a.route}</span><span class=\"meta\"> {rel}{hw}</span></div><span class=\"time\">{t}</span></div>")
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

    Option.iter renderArrivals model.lastArrivals

let bindStopSelect (dispatch: Dispatch<Msg>) =
    stopSelect.addEventListener("change", fun () ->
        dispatch (SelectStop stopSelect.value))
