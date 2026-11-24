import { addDays, colomboToday } from "./calendar";

/*  Near me (F33), without the phone or the screen: the order of asking, what a refusal means, and
    what leaves the phone. The twin of the web's locate() and find-query.ts. The point is rounded
    here, before anything else sees it, and lives only in the screen's memory (G3, G8).          */

/** The radii the API accepts (`AvailabilitySearch.RadiiKm`). */
export const RADII_KM = [5, 10, 25, 50] as const;
export type RadiusKm = (typeof RADII_KM)[number];
export const DEFAULT_RADIUS_KM: RadiusKm = 10;

/** How long a donor waits for a position, as on the web. A phone indoors may never answer. */
export const LOCATE_WAIT_MS = 10_000;

export type NearPoint = { latitude: number; longitude: number };
export type LocationProblem = "denied" | "outside" | "unavailable";
export type Located = { ok: true; point: NearPoint } | { ok: false; problem: LocationProblem };

/** The phone, as near me needs it. `lastKnown` is a recent position, or null if it has none. */
export type LocationDevice = {
  requestPermission(): Promise<boolean>;
  lastKnown(): Promise<NearPoint | null>;
  current(): Promise<NearPoint>;
};

/** Which of the two days the donor is looking at. */
export type NearDay = "today" | "tomorrow";

/** The day's date in Colombo, the API's calendar. */
export function nearDate(day: NearDay, now: Date = new Date()): string {
  const today = colomboToday(now);
  return day === "today" ? today : addDays(today, 1);
}

/** A coordinate to 3 decimals, about 100 m (G3): enough to find what is near, not the donor's door. */
export function roundCoordinate(value: number): number {
  return Math.round(value * 1000) / 1000;
}

/** Sri Lanka's box, the one the API holds a searched point to (`GeoPoint.InSriLanka`). */
export function inSriLanka({ latitude, longitude }: NearPoint): boolean {
  return latitude >= 5.5 && latitude <= 10 && longitude >= 79.3 && longitude <= 82.1;
}

/**
 * A distance to one decimal, as a string so that the message shows it as it is. Not Intl: a
 * phone's Intl depends on its OS version (dates.ts).
 */
export function formatKm(km: number): string {
  return String(Math.round(km * 10) / 10);
}

function position(device: LocationDevice, waitMs: number): Promise<NearPoint> {
  let timer: ReturnType<typeof setTimeout> | undefined;
  const expired = new Promise<never>((_, reject) => {
    timer = setTimeout(() => reject(new Error("timeout")), waitMs);
  });
  const found = device
    .lastKnown()
    .catch(() => null)
    .then((known) => known ?? device.current());
  return Promise.race([found, expired]).finally(() => clearTimeout(timer));
}

/** Asks for the phone's position. Every way this can go wrong comes back as a problem, never a throw. */
export async function locate(device: LocationDevice, waitMs: number = LOCATE_WAIT_MS): Promise<Located> {
  try {
    if (!(await device.requestPermission())) return { ok: false, problem: "denied" };

    const { latitude, longitude } = await position(device, waitMs);
    const point = { latitude: roundCoordinate(latitude), longitude: roundCoordinate(longitude) };
    return inSriLanka(point) ? { ok: true, point } : { ok: false, problem: "outside" };
  } catch {
    return { ok: false, problem: "unavailable" };
  }
}
