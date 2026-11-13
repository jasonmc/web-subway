module State

open System
open Time
open Geography

type Direction =
    | Northbound
    | Southbound
    | Other

type Arrival =
    { time: DateTimeOffset
      route: string
      direction: Direction
      tripId: string option
      destinationStopId: string option }

type ArrivalGroup =
    { Northbound: Arrival array
      Southbound: Arrival array
      Other: Arrival array }

type FeedMeta =
    { headerTimestampMs: float<milliseconds> option }

type StopOption =
    { id: string
      name: string
      latitude: float<latitude> option
      longitude: float<longitude> option }

type TrainStopCall =
    { stopId: string
      arriveSec: float<seconds>
      departSec: float<seconds> }

type RawTrainTrip =
    { tripId: string
      direction: Direction
      calls: TrainStopCall array }

type FeedSnapshot =
    { availableStops: Set<string>
      arrivalsByStop: Map<string, Arrival array>
      meta: FeedMeta
      rawTrips: RawTrainTrip array }

type EstimatedTrain =
    { tripId: string
      direction: Direction
      latitude: float<latitude>
      longitude: float<longitude>
      spinePos: float
      headingDeg: float
      isStopped: bool
      nextStopId: string }

type StatusDisplay =
    | Loading
    | FeedAge of headerTimestampMs: float<milliseconds> option
    | ErrorMessage of string

type MobileView =
    | ArrivalsView
    | MapView

// Immutable Elmish Model
type Model =
    { now: DateTimeOffset
      stopOptions: StopOption array
      stopLookup: Map<string, StopOption>
      defaultStop: StopOption
      selectedStopPrefix: string
      hasUrlStop: bool
      manuallySelected: bool
      fetching: bool
      availableStops: Set<string> option
      lastFeed: FeedSnapshot option
      lastArrivals: ArrivalGroup option
      lastFeedMeta: FeedMeta option
      lastFeedTimestampMs: float<milliseconds> option
      lastVisibleTotal: int
      rawTrips: RawTrainTrip array
      spinePosByTrip: Map<string, float * float<seconds>>
      mapTrains: EstimatedTrain array
      userLocation: Coordinates option
      spotlightTripId: string option
      mobileView: MobileView
      status: StatusDisplay }

// Elmish Messages
type Msg =
    | SelectMobileView of MobileView
    | SelectStop of stopId: string
    | GeoResolved of (string * Coordinates) option
    | RefreshFeed of showLoading: bool
    | FeedLoaded of Result<DateTimeOffset * FeedSnapshot, string>
    | SetSpotlight of string option
    | ToggleSpotlight of string
    | Tick of DateTimeOffset
