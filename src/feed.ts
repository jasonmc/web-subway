import * as transitRealtime from "gtfs-realtime-bindings";
import { STOP_SUFFIX_RE } from "./config";
import { Arrival, ArrivalGroup, Direction, FeedMeta } from "./state";

const { transit_realtime } = transitRealtime;

export function decodeFeed(buf: ArrayBuffer): transit_realtime.FeedMessage {
  return transit_realtime.FeedMessage.decode(new Uint8Array(buf));
}

function stopIdPrefixOf(stopId: string): string {
  return stopId.replace(STOP_SUFFIX_RE, "");
}

export function stopsInFeed(feed: transit_realtime.FeedMessage): Set<string> {
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

export function formatRelative(target: Date): string {
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

export function formatAge(target: Date): string {
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

export function headway(current: Arrival, next?: Arrival): string {
  if (!next) return "";
  const diffMs = next.time.getTime() - current.time.getTime();
  const diffMin = Math.round(diffMs / 60000);
  return diffMin > 0 ? ` • next in ${diffMin}m` : "";
}

export function parseFeed(
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
