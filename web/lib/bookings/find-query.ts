import { addDays, colomboToday } from "./calendar";

/*  The search page's state in the address (plan 06, G11): the day, the time of day, the kind of
    place and where to look, so the back button undoes a filter and a link can be shared ("lunch
    near Galle on 14 March"). Parsing is forgiving, like the admin lists: a value the page does not
    know falls back to its default rather than breaking it.                                      */

export const timesOfDay = ["any", "morning", "midday", "evening"] as const;
export type TimeOfDay = (typeof timesOfDay)[number];

/** The radii a donor may choose (G1), in km. */
export const radii = [5, 10, 25, 50] as const;
export type Radius = (typeof radii)[number];
export const DEFAULT_RADIUS: Radius = 10;

export type Where =
  | { kind: "anywhere" }
  | { kind: "district"; district: string }
  | { kind: "near"; latitude: number; longitude: number; radiusKm: Radius };

export type FindQuery = {
  /** yyyy-MM-dd, Colombo. */
  date: string;
  time: TimeOfDay;
  /** An institution type's code. */
  type: string | null;
  where: Where;
  /** On a phone, the list or the map; a wide screen shows both. */
  view: "list" | "map";
  /** A memorial observance being found a place for (plan 07, H6): it is on `date`, and "Book" carries it. */
  memorial: Memorial | null;
};

/** A memorial plan's publicId and one of its observances' rule codes. */
export type Memorial = { plan: string; rule: string };

const PUBLIC_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const RULE = /^[A-Z0-9_]{1,50}$/;

/** The memorial named by `plan` and `rule`, both well formed, or null. */
export function parseMemorial(params: Params): Memorial | null {
  const plan = params.get("plan");
  const rule = params.get("rule");
  return plan && rule && PUBLIC_ID.test(plan) && RULE.test(rule) ? { plan: plan.toLowerCase(), rule } : null;
}

/** `&plan=...&rule=...` for a link that keeps the memorial, or nothing. */
export function memorialSearch(memorial: Memorial | null): string {
  return memorial ? `&plan=${encodeURIComponent(memorial.plan)}&rule=${encodeURIComponent(memorial.rule)}` : "";
}

type Params = { get(name: string): string | null };

/** Tomorrow: most meals are booked a day ahead at the earliest, so today would mostly show nothing. */
export function defaultDate(today = colomboToday()): string {
  return addDays(today, 1);
}

/**
 * A coordinate to 3 decimals, about 100 m (G3): enough to find what is near, not enough to find
 * the donor's door. Every point the page sends or writes into the address is rounded first.
 */
export function roundCoordinate(value: number): number {
  return Math.round(value * 1000) / 1000;
}

/** Sri Lanka's box, the one the API holds a searched point to (`GeoPoint.InSriLanka`). */
export function inSriLanka(latitude: number, longitude: number): boolean {
  return latitude >= 5.5 && latitude <= 10 && longitude >= 79.3 && longitude <= 82.1;
}

const code = (value: string | null): string | null => (value && /^[A-Za-z0-9_-]{1,30}$/.test(value) ? value : null);

function point(value: string | null): { latitude: number; longitude: number } | null {
  const match = value?.match(/^(-?\d{1,2}(?:\.\d+)?),(-?\d{1,3}(?:\.\d+)?)$/);
  if (!match) return null;
  const latitude = roundCoordinate(Number(match[1]));
  const longitude = roundCoordinate(Number(match[2]));
  return inSriLanka(latitude, longitude) ? { latitude, longitude } : null;
}

export function parseFindQuery(params: Params, today = colomboToday()): FindQuery {
  const date = params.get("date");
  const time = params.get("time");
  const radius = Number(params.get("radius"));
  const near = point(params.get("near"));
  const district = code(params.get("district"));

  return {
    date: date && /^\d{4}-\d{2}-\d{2}$/.test(date) && date >= today ? date : defaultDate(today),
    time: (timesOfDay as readonly string[]).includes(time ?? "") ? (time as TimeOfDay) : "any",
    type: code(params.get("type")),
    where: near
      ? { kind: "near", ...near, radiusKm: (radii as readonly number[]).includes(radius) ? (radius as Radius) : DEFAULT_RADIUS }
      : district
        ? { kind: "district", district }
        : { kind: "anywhere" },
    view: params.get("view") === "map" ? "map" : "list",
    memorial: parseMemorial(params),
  };
}

/** The page's address for `query`: defaults left out, so a plain search has a plain URL. */
export function toFindSearch(query: FindQuery, today = colomboToday()): string {
  const params = new URLSearchParams();
  if (query.date !== defaultDate(today)) params.set("date", query.date);
  if (query.time !== "any") params.set("time", query.time);
  if (query.type) params.set("type", query.type);
  if (query.where.kind === "near") {
    params.set("near", `${roundCoordinate(query.where.latitude)},${roundCoordinate(query.where.longitude)}`);
    if (query.where.radiusKm !== DEFAULT_RADIUS) params.set("radius", String(query.where.radiusKm));
  } else if (query.where.kind === "district") {
    params.set("district", query.where.district);
  }
  if (query.view === "map") params.set("view", "map");
  if (query.memorial) {
    params.set("plan", query.memorial.plan);
    params.set("rule", query.memorial.rule);
  }

  const text = params.toString();
  return text ? `?${text}` : "";
}

function filters(query: FindQuery): URLSearchParams {
  const params = new URLSearchParams();
  if (query.time !== "any") params.set("time", query.time);
  if (query.type) params.set("type", query.type);
  if (query.where.kind === "near") {
    params.set("near", `${roundCoordinate(query.where.latitude)},${roundCoordinate(query.where.longitude)}`);
    params.set("radiusKm", String(query.where.radiusKm));
  } else if (query.where.kind === "district") {
    params.set("district", query.where.district);
  }
  return params;
}

/** The query string of `GET /api/search` for `query`'s day. */
export function searchApiQuery(query: FindQuery): string {
  const params = filters(query);
  params.set("date", query.date);
  return params.toString();
}

/** The query string of `GET /api/search/month` for `month` (yyyy-MM) under `query`'s filters. */
export function monthApiQuery(query: FindQuery, month: string): string {
  const params = filters(query);
  params.set("month", month);
  return params.toString();
}
