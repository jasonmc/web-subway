module State

open Fable.Core

type Direction = Northbound | Southbound | Other

type Arrival = {
    time: JS.Date
    route: string
    direction: Direction
    label: string option
}

type ArrivalGroup = {
    Northbound: Arrival array
    Southbound: Arrival array
    Other: Arrival array
}

type FeedMeta = { headerTimestampMs: float option }

type StopOption = {
    id: string
    name: string
    latitude: float option
    longitude: float option
}

type StatusDisplay =
    | Loading
    | FeedAge of headerTimestampMs: float option
    | ErrorMessage of string

// Immutable Elmish Model
type Model = {
    stopOptions: StopOption array
    stopLookup: Map<string, StopOption>
    defaultStop: StopOption
    selectedStopPrefix: string
    hasUrlStop: bool
    manuallySelected: bool
    fetching: bool
    availableStops: Set<string> option
    lastArrivals: ArrivalGroup option
    lastFeedMeta: FeedMeta option
    lastFeedTimestampMs: float option
    lastVisibleTotal: int
    status: StatusDisplay
}

// Elmish Messages
type Msg =
    | SelectStop of stopId: string
    | GeoResolved of stopId: string option
    | RefreshFeed of showLoading: bool
    | FeedLoaded of Result<Set<string> * ArrivalGroup * FeedMeta, string>
    | TickAge
