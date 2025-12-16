import * as transitRealtime from "gtfs-realtime-bindings";
import stopsCsvGzUrl from "../all_stops.csv.gz?url";
import "./style.css";

const { transit_realtime } = transitRealtime;

const DEFAULT_FEED_URL =
  "https://api-endpoint.mta.info/Dataservice/mtagtfsfeeds/nyct%2Fgtfs-g";
const DEFAULT_STOP_ID_PREFIX = "G26";
const STOP_SUFFIX_RE = /[NS]$/;

type Direction = "Northbound" | "Southbound" | "Other";

type Arrival = {
  time: Date;
  route: string;
  direction: Direction;
  label?: string;
};

type ArrivalGroup = Record<Direction, Arrival[]>;

type FeedMeta = {
  headerTimestampMs: number | null;
};

const statusEl = document.querySelector<HTMLElement>("#status");
const listsEl = document.querySelector<HTMLElement>("#lists");
const stopSelect = document.querySelector<HTMLSelectElement>("#stop-select");
const refreshBtn = document.querySelector<HTMLButtonElement>("#refresh");
const stopNameEl = document.querySelector<HTMLElement>("#stop-name");
let refreshTimer: number | undefined;
let ageTimer: number | undefined;
let fetching = false;
let selectedStopPrefix = DEFAULT_STOP_ID_PREFIX;
let lastFeedTimestampMs: number | null = null;
let lastVisibleTotal = 0;

type StopOption = { id: string; name: string };

function parseStops(csv: string): StopOption[] {
  const rows = csv.trim().split(/\r?\n/);
  const [, ...dataRows] = rows;
  const byId = new Map<string, StopOption>();

  for (const row of dataRows) {
    if (!row.trim()) continue;
    const [stopId, stopName, , , locationType, parentStation] = row.split(",");
    if (!stopId || !stopName) continue;

    const baseId =
      parentStation ||
      (locationType === "1" ? stopId : stopId.replace(/[NS]$/, ""));

    if (!byId.has(baseId)) {
      byId.set(baseId, { id: baseId, name: stopName });
    }
  }

  return Array.from(byId.values()).sort((a, b) =>
    a.name.localeCompare(b.name, "en")
  );
}

function stopFromLocation(): string | null {
  try {
    const url = new URL(window.location.href);
    const hash = url.hash.startsWith("#") ? url.hash.slice(1) : url.hash;
    return hash || null;
  } catch {
    return null;
  }
}

let stopOptions: StopOption[] = [];
let defaultStop: StopOption = {
  id: DEFAULT_STOP_ID_PREFIX,
  name: "Default stop"
};

async function loadStopsCsv(): Promise<string> {
  const response = await fetch(stopsCsvGzUrl);
  if (!response.ok) throw new Error(`Stops fetch failed: ${response.status}`);
  const buf = await response.arrayBuffer();

  const tryDecompress = async () => {
    if (!("DecompressionStream" in window)) {
      throw new Error("DecompressionStream not supported in this browser.");
    }
    const stream = new Blob([buf]).stream().pipeThrough(
      new DecompressionStream("gzip")
    );
    return new Response(stream).text();
  };

  let text: string;
  try {
    text = await tryDecompress();
  } catch {
    text = new TextDecoder().decode(buf);
  }

  if (!text.includes(",")) throw new Error("Invalid stops csv");
  return text;
}

async function initStops() {
  const csv = await loadStopsCsv();
  stopOptions = parseStops(csv);
  defaultStop =
    stopOptions.find((s) => s.id === DEFAULT_STOP_ID_PREFIX) ??
    stopOptions[0] ??
    { id: DEFAULT_STOP_ID_PREFIX, name: "Default stop" };
  const urlStop = stopFromLocation();
  const validUrlStop = stopOptions.some((s) => s.id === urlStop)
    ? urlStop
    : null;
  selectedStopPrefix = validUrlStop ?? defaultStop.id;
}

function currentStopName(): string {
  return (
    stopOptions.find((s) => s.id === selectedStopPrefix)?.name ??
    `Stop ${selectedStopPrefix}`
  );
}

function updateStopLabel() {
  if (stopNameEl) {
    stopNameEl.textContent = currentStopName();
  }
}

function syncStopParam() {
  try {
    const url = new URL(window.location.href);
    url.hash = selectedStopPrefix;
    window.history.replaceState({}, "", url);
  } catch {
    // ignore if history/location not available
  }
}

function stopIdPrefixOf(stopId: string): string {
  return stopId.replace(STOP_SUFFIX_RE, "");
}

function stopsWithArrivals(feed: transit_realtime.FeedMessage): Set<string> {
  const ids = new Set<string>();
  for (const entity of feed.entity) {
    const updates = entity.tripUpdate?.stopTimeUpdate ?? [];
    for (const stu of updates) {
      if (!stu.stopId) continue;
      ids.add(stopIdPrefixOf(stu.stopId));
    }
  }
  return ids;
}

// Coerce protobuf Long/number/undefined to number (seconds since epoch).
function toInt(value: unknown): number | undefined {
  if (typeof value === "number") return value;
  if (typeof value === "bigint") return Number(value);
  if (value && typeof value === "object" && "toNumber" in value) {
    try {
      return (value as { toNumber: () => number }).toNumber();
    } catch {
      return undefined;
    }
  }
  return undefined;
}

function formatRelative(target: Date): string {
  const now = new Date();
  const diffMs = target.getTime() - now.getTime();
  const diffMin = Math.round(diffMs / 60000);
  if (diffMin <= 0) return "due";
  if (diffMin === 1) return "in 1 min";
  if (diffMin < 60) return `in ${diffMin} min`;
  const hours = Math.floor(diffMin / 60);
  const minutes = diffMin % 60;
  return minutes === 0 ? `in ${hours}h` : `in ${hours}h ${minutes}m`;
}

function formatAge(target: Date): string {
  const now = new Date();
  const diffMs = now.getTime() - target.getTime();
  const diffSec = Math.round(diffMs / 1000);
  if (diffSec <= 0) return "just now";
  if (diffSec < 60) return `${diffSec}s ago`;
  const diffMin = Math.round(diffSec / 60);
  if (diffMin === 1) return "1 min ago";
  if (diffMin < 60) return `${diffMin} min ago`;
  const hours = Math.floor(diffMin / 60);
  const minutes = diffMin % 60;
  return minutes === 0 ? `${hours}h ago` : `${hours}h ${minutes}m ago`;
}

function headway(current: Arrival, next?: Arrival): string {
  if (!next) return "";
  const diffMs = next.time.getTime() - current.time.getTime();
  const diffMin = Math.round(diffMs / 60000);
  return diffMin > 0 ? ` • next in ${diffMin}m` : "";
}

function parseFeed(
  feed: transit_realtime.FeedMessage,
  stopIdPrefix: string
): {
  arrivals: ArrivalGroup;
  meta: FeedMeta;
} {
  const now = new Date();
  const north: Arrival[] = [];
  const south: Arrival[] = [];
  const other: Arrival[] = [];

  const headerTs = toInt(feed.header?.timestamp ?? undefined) ?? null;
  const headerTimestampMs = headerTs ? headerTs * 1000 : null;

  for (const entity of feed.entity) {
    const tripUpdate = entity.tripUpdate;
    if (!tripUpdate) continue;

    const route = tripUpdate.trip?.routeId ?? "?";
    const baseDirection: Direction = (() => {
      const dir = toInt(tripUpdate.trip?.directionId);
      if (dir === 0) return "Northbound";
      if (dir === 1) return "Southbound";
      return "Other";
    })();

    for (const stu of tripUpdate.stopTimeUpdate ?? []) {
      const stopId = stu.stopId;
      if (!stopId || !stopId.startsWith(stopIdPrefix)) continue;

      const rawTs = toInt(stu.arrival?.time) ?? toInt(stu.departure?.time);
      if (!rawTs) continue;

      const when = new Date(rawTs * 1000);
      if (when.getTime() < now.getTime()) continue;

      const dir: Direction = (() => {
        const lastChar = stopId.at(-1);
        if (lastChar === "N") return "Northbound";
        if (lastChar === "S") return "Southbound";
        return baseDirection;
      })();

      const collection =
        dir === "Northbound" ? north : dir === "Southbound" ? south : other;
      collection.push({ time: when, route, direction: dir });
    }
  }

  const sortByTime = (a: Arrival, b: Arrival) =>
    a.time.getTime() - b.time.getTime();
  north.sort(sortByTime);
  south.sort(sortByTime);
  other.sort(sortByTime);

  return {
    arrivals: {
      Northbound: north,
      Southbound: south,
      Other: other
    },
    meta: { headerTimestampMs }
  };
}

function updateStatusText(visibleTotal: number, headerTimestampMs: number | null) {
  if (!statusEl) return;
  statusEl.className = "status";
  const stopName = currentStopName();
  const ageText = headerTimestampMs ? formatAge(new Date(headerTimestampMs)) : "unknown";
  statusEl.textContent =
    visibleTotal === 0
      ? `No arrivals found for ${stopName} right now.`
      : `${stopName} • Feed age: ${ageText} • Showing ${visibleTotal} arrival${
          visibleTotal === 1 ? "" : "s"
        }`;
}

function render(arrivals: ArrivalGroup, meta: FeedMeta) {
  if (!listsEl) return;
  const cards: Array<{ title: string; icon: string; items: Arrival[] }> = [
    { title: "Northbound", icon: "⬆️", items: arrivals.Northbound.slice(0, 3) },
    { title: "Southbound", icon: "⬇️", items: arrivals.Southbound.slice(0, 3) },
    { title: "Other", icon: "⬜", items: arrivals.Other.slice(0, 3) }
  ];

  const visibleTotal = cards.reduce((sum, card) => sum + card.items.length, 0);
  lastVisibleTotal = visibleTotal;
  lastFeedTimestampMs = meta.headerTimestampMs;

  updateStatusText(visibleTotal, meta.headerTimestampMs);

  listsEl.innerHTML = cards
    .filter((c) => c.items.length > 0)
    .map(
      (card) => `
      <article class="card">
        <h2>${card.icon} ${card.title}</h2>
        ${card.items
          .map((arrival, idx) => {
            const next = card.items[idx + 1];
            return `
              <div class="arrival">
                <div>
                  <span class="route">${arrival.route}</span>
                  <span class="meta"> ${formatRelative(arrival.time)}${headway(
                    arrival,
                    next
                  )}</span>
                </div>
                <span class="time">${arrival.time.toLocaleTimeString([], {
                  hour: "numeric",
                  minute: "2-digit"
                })}</span>
              </div>
            `;
          })
          .join("")}
      </article>
    `
    )
    .join("");

  if (listsEl.innerHTML.trim() === "") {
    listsEl.innerHTML =
      `<article class="card"><p class="meta">No arrivals for ${currentStopName()} in the feed.</p></article>`;
  }
}

function setError(message: string) {
  if (statusEl) {
    statusEl.className = "status error";
    statusEl.textContent = message;
  }
  if (listsEl) {
    listsEl.innerHTML = "";
  }
}

function scheduleRefresh(intervalMs = 5000) {
  if (refreshTimer) {
    clearInterval(refreshTimer);
  }
  refreshTimer = window.setInterval(() => {
    void load(false);
  }, intervalMs);
}

function scheduleAgeTicker(intervalMs = 1000) {
  if (ageTimer) {
    clearInterval(ageTimer);
  }
  ageTimer = window.setInterval(() => {
    if (lastFeedTimestampMs !== null) {
      updateStatusText(lastVisibleTotal, lastFeedTimestampMs);
    }
  }, intervalMs);
}

function updateStopSelect(available?: Set<string>) {
  if (!stopSelect) return;

  const filtered =
    available && available.size > 0
      ? stopOptions.filter((s) => available.has(s.id))
      : stopOptions;

  stopSelect.innerHTML = filtered
    .map(
      (stop) =>
        `<option value="${stop.id}" ${
          stop.id === selectedStopPrefix ? "selected" : ""
        }>${stop.name}</option>`
    )
    .join("");

  if (!filtered.some((s) => s.id === selectedStopPrefix) && filtered[0]) {
    selectedStopPrefix = filtered[0].id;
  }

  stopSelect.value = selectedStopPrefix;
  syncStopParam();
}

function initStopSelect() {
  updateStopSelect();

  stopSelect.addEventListener("change", () => {
    selectedStopPrefix = stopSelect.value;
    updateStopLabel();
    syncStopParam();
    void load();
    scheduleRefresh();
  });
}

async function load(showLoading = true) {
  if (fetching) return;
  fetching = true;
  try {
    if (statusEl && showLoading) {
      statusEl.className = "status";
      statusEl.textContent = "Loading…";
    }

    const feedUrl = import.meta.env.VITE_MTA_FEED_URL ?? DEFAULT_FEED_URL;

    const response = await fetch(feedUrl, { mode: "cors" });
    if (!response.ok) {
      throw new Error(`Request failed: ${response.status} ${response.statusText}`);
    }

    const buf = await response.arrayBuffer();
    const feed = transit_realtime.FeedMessage.decode(new Uint8Array(buf));
    const availableStops = stopsWithArrivals(feed);
    if (availableStops.size > 0) {
      updateStopSelect(availableStops);
      updateStopLabel();
      syncStopParam();
    }
    const { arrivals, meta } = parseFeed(feed, selectedStopPrefix);
    render(arrivals, meta);
  } catch (err) {
    const message =
      err instanceof Error ? err.message : "Failed to load arrivals.";
    setError(message);
    console.error(err);
  } finally {
    fetching = false;
  }
}

refreshBtn?.addEventListener("click", () => {
  void load();
  scheduleRefresh();
});

void (async () => {
  try {
    await initStops();
    updateStopLabel();
    initStopSelect();
    syncStopParam();
    await load();
    scheduleRefresh();
    scheduleAgeTicker();
  } catch (err) {
    console.error(err);
    setError("Failed to load stops list.");
  }
})();
