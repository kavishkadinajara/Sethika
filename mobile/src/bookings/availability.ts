import type { MessageKey } from "@/i18n/translate";

import type { DayAvailability, DayRefusal, PublicMealSlot } from "./types";

/*  What one day of a slot's month looks like on the bowl calendar (plan 03, E24 and E32). The
    API has already judged the day; this only turns its answer into a picture and words:
      open     an empty bowl: nobody has booked it yet (or the slot takes one sponsor)
      filling  a bowl part full: a shared slot with some places taken
      full     a full bowl: no booking can be taken
      closed   no bowl, a plain date: not offered that day, blocked, too soon, too far
      unknown  the month has not loaded, or the API did not list the day
    The picture is never the only signal: every state has words (dayWords), read out as the
    day's label and shown when the day is tapped.                                            */

export type DayState =
  | { kind: "open"; fill: 0; placesLeft: number | null }
  | { kind: "filling"; fill: number; placesLeft: number }
  | { kind: "full"; fill: 1 }
  | { kind: "closed"; reason: DayRefusal }
  | { kind: "unknown" };

export function dayState(slot: PublicMealSlot, day: DayAvailability | undefined): DayState {
  if (!day) return { kind: "unknown" };

  if (!day.bookable) {
    // The API names "slot_full" for a day it would refuse every booking on (E24).
    return day.reason === "slot_full" ? { kind: "full", fill: 1 } : { kind: "closed", reason: day.reason ?? "slot_inactive" };
  }

  if (slot.bookingMode !== "Shared" || slot.maxSponsors <= 0) return { kind: "open", fill: 0, placesLeft: null };

  const taken = Math.min(1, Math.max(0, (slot.maxSponsors - day.sponsors) / slot.maxSponsors));
  return taken > 0
    ? { kind: "filling", fill: taken, placesLeft: day.sponsors }
    : { kind: "open", fill: 0, placesLeft: day.sponsors };
}

/** Whether choosing the day opens the booking sheet. */
export function canChoose(state: DayState): boolean {
  return state.kind === "open" || state.kind === "filling";
}

/** The day's state in words, as a message; null when there is nothing to say yet. */
export function dayWords(state: DayState): { key: MessageKey; values?: Record<string, number> } | null {
  switch (state.kind) {
    case "open":
      return state.placesLeft === null ? { key: "Discover.open" } : { key: "Discover.placesLeft", values: { count: state.placesLeft } };
    case "filling":
      return { key: "Discover.placesLeft", values: { count: state.placesLeft } };
    case "full":
      return { key: "Discover.reason.slot_full" };
    case "closed":
      return { key: `Discover.reason.${state.reason}` };
    case "unknown":
      return null;
  }
}

/** The month's days for one slot, by date; undefined while the month is loading. */
export function slotDays(
  availability: { slots: { mealSlot: string; days: DayAvailability[] }[] } | undefined,
  mealSlot: string,
): Map<string, DayAvailability> | undefined {
  if (!availability) return undefined;
  const days = availability.slots.find((slot) => slot.mealSlot === mealSlot)?.days ?? [];
  return new Map(days.map((day) => [day.date, day]));
}

/** The people one booking may bring on `day`: the slot's own range, capped by what the day has left. */
export function headcountLimits(slot: PublicMealSlot, day: DayAvailability): { min: number; max: number } {
  return { min: slot.minHeadcount, max: Math.max(slot.minHeadcount, Math.min(slot.maxHeadcount, day.headcount)) };
}

/** The typed headcount if it is a whole number in range, else null. */
export function readHeadcount(text: string, limits: { min: number; max: number }): number | null {
  if (!/^\d+$/.test(text.trim())) return null;
  const people = Number(text.trim());
  return people >= limits.min && people <= limits.max ? people : null;
}

/** The headcount after a tap on − or +, kept in range; a blank or garbled field starts from the minimum. */
export function stepHeadcount(text: string, by: number, limits: { min: number; max: number }): string {
  const current = /^\d+$/.test(text.trim()) ? Number(text.trim()) : limits.min;
  return String(Math.min(limits.max, Math.max(limits.min, current + by)));
}
