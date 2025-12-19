import "./style.css";

import { DEFAULT_FEED_URL } from "./config";
import { decodeFeed, parseFeed, stopsInFeed } from "./feed";
import { initStops } from "./stops";
import { state } from "./state";
import { scheduleAgeTicker, scheduleRefresh } from "./timers";
import {
  initStopSelect,
  render,
  setError,
  setSelectedStop,
  setStatusText,
  updateStatusText,
  updateStopSelect
} from "./ui";

async function load(showLoading = true) {
  if (state.fetching) return;
  state.fetching = true;
  try {
    if (showLoading) {
      setStatusText("Loading…");
    }

    const feedUrl = import.meta.env.VITE_MTA_FEED_URL ?? DEFAULT_FEED_URL;

    const response = await fetch(feedUrl, { mode: "cors" });
    if (!response.ok) {
      throw new Error(`Request failed: ${response.status} ${response.statusText}`);
    }

    const buf = await response.arrayBuffer();
    const feed = decodeFeed(buf);
    const availableStops = stopsInFeed(feed);
    if (availableStops.size > 0) {
      updateStopSelect(Array.from(availableStops));
    }
    const { arrivals, meta } = parseFeed(feed, state.selectedStopPrefix);
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
    await initStops();
    setSelectedStop(state.selectedStopPrefix);
    initStopSelect(() => {
      void load();
      scheduleRefresh(load);
    });
    await load();
    scheduleRefresh(load);
    scheduleAgeTicker(() => {
      if (state.lastFeedTimestampMs !== null) {
        updateStatusText(state.lastVisibleTotal, state.lastFeedTimestampMs);
      }
    });
  } catch (err) {
    console.error(err);
    setError("Failed to load stops list.");
  }
})();
