import { PREFERRED_STOP_ORDER } from "./config";
import { state } from "./state";

function nearestStop(latitude: number, longitude: number): string | null {
  if (
    !Number.isFinite(latitude) || !Number.isFinite(longitude) ||
    Math.abs(latitude) > 90 || Math.abs(longitude) > 180
  ) {
    return null;
  }

  const radians = (degrees: number) => degrees * Math.PI / 180;
  let nearest: string | null = null;
  let shortestDistance = Infinity;
  for (const id of PREFERRED_STOP_ORDER) {
    const station = state.stopLookup.get(id);
    if (station?.latitude === undefined || station.longitude === undefined) continue;
    // Haversine distance without the common Earth-radius factor.
    const distance =
      Math.sin(radians(station.latitude - latitude) / 2) ** 2 +
      Math.cos(radians(latitude)) * Math.cos(radians(station.latitude)) *
      Math.sin(radians(station.longitude - longitude) / 2) ** 2;
    if (distance < shortestDistance) {
      nearest = id;
      shortestDistance = distance;
    }
  }
  return nearest;
}

export function nearestStopFromDevice(): Promise<string | null> {
  return new Promise((resolve) => {
    if (!navigator.geolocation) {
      resolve(null);
      return;
    }
    try {
      navigator.geolocation.getCurrentPosition(
        ({ coords }) => resolve(nearestStop(coords.latitude, coords.longitude)),
        () => resolve(null),
        { timeout: 8000, maximumAge: 60000 }
      );
    } catch {
      resolve(null);
    }
  });
}
