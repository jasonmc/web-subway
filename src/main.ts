import "./style.css";

import { ARRIVALS_PER_DIRECTION, DEFAULT_FEED_URL } from "./config";
import { decodeFeed, parseFeed, stopsInFeed } from "./feed";
import { initStops } from "./stops";
import { nearestStopFromDevice } from "./location";
import { state } from "./state";
import { scheduleAgeTicker, scheduleRefresh } from "./timers";
import {
  initStopSelect,
  render,
  setError,
  setSelectedStop,
  setStatusText,
  updateStopSelect
} from "./ui";
import { formatAge } from "./feed";
import { ArrivalGroup } from "./state";

async function fetchFeed(): Promise<ArrayBuffer> {
  const feedUrl = import.meta.env.VITE_MTA_FEED_URL ?? DEFAULT_FEED_URL;
  const response = await fetch(feedUrl, { mode: "cors" });
  if (!response.ok) {
    throw new Error(`Request failed: ${response.status} ${response.statusText}`);
  }
  return response.arrayBuffer();
}

function updateFeedState(meta: { headerTimestampMs: number | null }) {
  state.lastFeedTimestampMs = meta.headerTimestampMs;
  updateStatusFromTimestamp(meta.headerTimestampMs);
}

function statusTextForTimestamp(headerTimestampMs: number | null) {
  const ageText = headerTimestampMs
    ? formatAge(new Date(headerTimestampMs))
    : "unknown";
  return `Feed age: ${ageText}`;
}

function updateStatusFromTimestamp(headerTimestampMs: number | null) {
  setStatusText(statusTextForTimestamp(headerTimestampMs));
}

function updateVisibleTotal(arrivals: ArrivalGroup) {
  state.lastVisibleTotal =
    Math.min(arrivals.Northbound.length, ARRIVALS_PER_DIRECTION) +
    Math.min(arrivals.Southbound.length, ARRIVALS_PER_DIRECTION) +
    Math.min(arrivals.Other.length, ARRIVALS_PER_DIRECTION);
}

async function load(showLoading = true) {
  if (state.fetching) return;
  state.fetching = true;
  try {
    if (showLoading) {
      setStatusText("Loading…");
    }

    const buf = await fetchFeed();
    const feed = decodeFeed(buf);
    const availableStops = stopsInFeed(feed);
    if (availableStops.size > 0) {
      updateStopSelect(Array.from(availableStops));
    }
    const { arrivals, meta } = parseFeed(feed, state.selectedStopPrefix);
    updateVisibleTotal(arrivals);
    state.lastArrivals = arrivals;
    state.lastFeedMeta = meta;
    updateFeedState(meta);
    render(arrivals, meta);
  } catch (err) {
    // Do not clear existing display; only report the error.
    const message =
      err instanceof Error ? err.message : "Failed to load arrivals.";
    setStatusText(message, { error: true });
    console.error(err);
  } finally {
    state.fetching = false;
  }
}

void (async () => {
  try {
    const hasUrlStop = await initStops();
    let manuallySelected = false;
    setSelectedStop(state.selectedStopPrefix);
    initStopSelect(() => {
      manuallySelected = true;
      void load();
      scheduleRefresh(load);
    });
    if (!hasUrlStop) {
      void nearestStopFromDevice().then((nearest) => {
        if (!nearest || manuallySelected) return;
        setSelectedStop(nearest);
        updateStopSelect();
        void load(false);
      });
    }
    await load();
    scheduleRefresh(load);
    scheduleAgeTicker(() => {
      if (state.lastFeedTimestampMs !== null) {
        updateStatusFromTimestamp(state.lastFeedTimestampMs);
      }
      if (state.lastArrivals && state.lastFeedMeta) {
        render(state.lastArrivals, state.lastFeedMeta);
      }
    });
  } catch (err) {
    console.error(err);
    setError("Failed to load stops list.");
  }
})();
