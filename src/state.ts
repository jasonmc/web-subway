import { DEFAULT_STOP_ID_PREFIX } from "./config";

export type Direction = "Northbound" | "Southbound" | "Other";

export type Arrival = {
  time: Date;
  route: string;
  direction: Direction;
  label?: string;
};

export type ArrivalGroup = Record<Direction, Arrival[]>;

export type FeedMeta = {
  headerTimestampMs: number | null;
};

export type StopOption = { id: string; name: string };

export const state = {
  refreshTimer: undefined as number | undefined,
  ageTimer: undefined as number | undefined,
  fetching: false,
  selectedStopPrefix: DEFAULT_STOP_ID_PREFIX,
  lastFeedTimestampMs: null as number | null,
  lastVisibleTotal: 0,
  lastRenderedStopsKey: "",
  stopLookup: new Map<string, StopOption>(),
  stopOptions: [] as StopOption[],
  defaultStop: {
    id: DEFAULT_STOP_ID_PREFIX,
    name: "Default stop"
  } as StopOption
};
