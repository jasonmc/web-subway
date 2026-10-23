import stopsCsv from "../all_stops.csv?raw";
import { DEFAULT_STOP_ID_PREFIX } from "./config";
import { state } from "./state";
import { parseStops } from "./parse-stops";

function stopFromLocation(): string | null {
  try {
    const url = new URL(window.location.href);
    const hash = url.hash.startsWith("#") ? url.hash.slice(1) : url.hash;
    return hash || null;
  } catch {
    return null;
  }
}

export function initStops() {
  state.stopOptions = parseStops(stopsCsv);
  state.stopLookup = new Map(state.stopOptions.map((s) => [s.id, s]));
  state.defaultStop =
    state.stopOptions.find((s) => s.id === DEFAULT_STOP_ID_PREFIX) ??
    state.stopOptions[0] ??
    { id: DEFAULT_STOP_ID_PREFIX, name: "Default stop" };
  const urlStop = stopFromLocation();
  const validUrlStop = state.stopOptions.some((s) => s.id === urlStop)
    ? urlStop
    : null;
  state.selectedStopPrefix = validUrlStop ?? state.defaultStop.id;
  return validUrlStop !== null;
}

export function stopLabel(stopId: string): string {
  return (
    state.stopOptions.find((s) => s.id === stopId)?.name ??
    `Stop ${stopId}`
  );
}
