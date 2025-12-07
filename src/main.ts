import * as transitRealtime from "gtfs-realtime-bindings";
import "./style.css";

const { transit_realtime } = transitRealtime;

const DEFAULT_FEED_URL =
  "https://api-endpoint.mta.info/Dataservice/mtagtfsfeeds/nyct%2Fgtfs-g";
const GREENPOINT_STOP_ID_PREFIX = "G26";

type Direction = "Northbound" | "Southbound" | "Other";

type Arrival = {
  time: Date;
  route: string;
  direction: Direction;
  label?: string;
};

type ArrivalGroup = Record<Direction, Arrival[]>;

type FeedMeta = {
  ageText: string;
};

const statusEl = document.querySelector<HTMLElement>("#status");
const listsEl = document.querySelector<HTMLElement>("#lists");
const refreshBtn = document.querySelector<HTMLButtonElement>("#refresh");
let refreshTimer: number | undefined;
let fetching = false;

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
  const diffMin = Math.round(diffMs / 60000);
  if (diffMin <= 0) return "just now";
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

function parseFeed(feed: transit_realtime.FeedMessage): {
  arrivals: ArrivalGroup;
  meta: FeedMeta;
} {
  const now = new Date();
  const north: Arrival[] = [];
  const south: Arrival[] = [];
  const other: Arrival[] = [];

  const headerTs = toInt(feed.header?.timestamp ?? undefined) ?? null;
  const ageText = headerTs ? formatAge(new Date(headerTs * 1000)) : "unknown";

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
      if (!stopId || !stopId.startsWith(GREENPOINT_STOP_ID_PREFIX)) continue;

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
    meta: { ageText }
  };
}

function render(arrivals: ArrivalGroup, meta: FeedMeta) {
  if (!statusEl || !listsEl) return;

  const total =
    arrivals.Northbound.length +
    arrivals.Southbound.length +
    arrivals.Other.length;

  statusEl.className = "status";
  statusEl.textContent =
    total === 0
      ? "No arrivals found for G26 right now."
      : `Feed age: ${meta.ageText} • Showing ${total} arrival${
          total === 1 ? "" : "s"
        }`;

  const cards: Array<{ title: string; icon: string; items: Arrival[] }> = [
    { title: "Northbound", icon: "⬆️", items: arrivals.Northbound.slice(0, 3) },
    { title: "Southbound", icon: "⬇️", items: arrivals.Southbound.slice(0, 3) },
    { title: "Other", icon: "⬜", items: arrivals.Other.slice(0, 3) }
  ];

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
      '<article class="card"><p class="meta">No arrivals for G26 in the feed.</p></article>';
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
    const { arrivals, meta } = parseFeed(feed);
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

void load();
scheduleRefresh();
