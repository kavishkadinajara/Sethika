import { isIsoDate } from "./calendar";

/*  Where a donor comes back to after signing in from the booking sheet (plan 03, E33): the same
    institution, meal and day, as a route the institution screen reads again. Only the public ids
    travel (the slug, the slot's publicId, the date), never a database id.                     */

/** The institution screen opened on `slot` and `date`, with the booking sheet for that day. */
export function bookingReturnPath(slug: string, slot: string, date: string): string {
  const query = `slot=${encodeURIComponent(slot)}&date=${encodeURIComponent(date)}`;
  return `/i/${encodeURIComponent(slug)}?${query}`;
}

/**
 * `next` if it is a route inside this app that sign-in may send the donor to, else null. Only
 * an institution screen qualifies; anything else (another scheme, "//host", a path outside /i/)
 * is dropped, so a crafted link cannot use sign-in to send someone elsewhere.
 */
export function safeReturnPath(next: unknown): string | null {
  if (typeof next !== "string") return null;
  return /^\/i\/[A-Za-z0-9-]{1,150}(\?[A-Za-z0-9=&%._-]*)?$/.test(next) ? next : null;
}

/** The slot and date a return path (or a shared link) names, if both are well formed. */
export function chosenDay(params: { slot?: unknown; date?: unknown }): { slot: string; date: string } | null {
  const slot = typeof params.slot === "string" ? params.slot : null;
  const date = typeof params.date === "string" ? params.date : null;
  return slot && /^[0-9a-fA-F-]{36}$/.test(slot) && isIsoDate(date) ? { slot, date } : null;
}
