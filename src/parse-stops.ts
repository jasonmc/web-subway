import type { StopOption } from "./state";

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

export function parseStops(csv: string): StopOption[] {
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

