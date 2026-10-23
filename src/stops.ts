import stopsCsvGzUrl from "../all_stops.csv.gz?url";
import { DEFAULT_STOP_ID_PREFIX } from "./config";
import { state, StopOption } from "./state";

function parseCsvLine(line: string): string[] {
  const fields: string[] = [];
  let current = "";
  let inQuotes = false;

  for (let i = 0; i < line.length; i += 1) {
    const char = line[i];

    if (char === '"') {
      if (inQuotes && line[i + 1] === '"') {
        current += '"';
        i += 1;
      } else {
        inQuotes = !inQuotes;
      }
      continue;
    }

    if (char === "," && !inQuotes) {
      fields.push(current);
      current = "";
      continue;
    }

    current += char;
  }

  fields.push(current);
  return fields;
}

function parseStops(csv: string): StopOption[] {
  const rows = csv.trim().split(/\r?\n/);
  const [header, ...dataRows] = rows;
  const columns = parseCsvLine(header);
  const byId = new Map<string, StopOption>();

  for (const row of dataRows) {
    if (!row.trim()) continue;
    const fields = parseCsvLine(row);
    const field = (name: string) => fields[columns.indexOf(name)];
    const stopId = field("stop_id");
    const stopName = field("stop_name");
    const locationType = field("location_type");
    const parentStation = field("parent_station");
    if (!stopId || !stopName) continue;

    const baseId =
      parentStation ||
      (locationType === "1" ? stopId : stopId.replace(/[NS]$/, ""));

    if (!byId.has(baseId)) {
      const stop: StopOption = { id: baseId, name: stopName };
      const latitude = Number(field("stop_lat")?.trim() || NaN);
      const longitude = Number(field("stop_lon")?.trim() || NaN);
      if (
        Number.isFinite(latitude) && Math.abs(latitude) <= 90 &&
        Number.isFinite(longitude) && Math.abs(longitude) <= 180
      ) {
        stop.latitude = latitude;
        stop.longitude = longitude;
      }
      byId.set(baseId, stop);
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

export async function initStops() {
  const csv = await loadStopsCsv();
  state.stopOptions = parseStops(csv);
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
