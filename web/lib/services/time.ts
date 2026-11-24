import { addDays, COLOMBO } from "@/lib/bookings/calendar";

/*  Service times. The API stores UTC instants (V7, V28) and a donor picks a Colombo date and a start
    time on the 15-minute grid (V11). Nothing goes through the browser's own zone: a donor abroad
    still asks for the temple's 10:00.                                                              */

/** The grid, in minutes (TimeBlocks.Minutes). */
export const STEP_MINUTES = 15;

/** The start times offered, "05:00" to "21:00" every 15 minutes (V11: a default, not a rule). */
export function startTimes(from = "05:00", to = "21:00"): string[] {
  const times: string[] = [];
  for (let minutes = toMinutes(from); minutes <= toMinutes(to); minutes += STEP_MINUTES) {
    times.push(fromMinutes(minutes));
  }
  return times;
}

/** "HH:mm" plus a duration, for showing when a service would end ("10:00" + 180 = "13:00"). */
export function endTime(start: string, durationMinutes: number): string {
  return fromMinutes((toMinutes(start) + durationMinutes) % (24 * 60));
}

/** A UTC instant as Colombo wall time: the date and "HH:mm". */
export function colomboParts(utc: string): { date: string; time: string } {
  const parts = Object.fromEntries(
    new Intl.DateTimeFormat("en-CA", {
      timeZone: COLOMBO,
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
      hour: "2-digit",
      minute: "2-digit",
      hourCycle: "h23",
    })
      .formatToParts(new Date(utc))
      .map((part) => [part.type, part.value]),
  );
  return { date: `${parts.year}-${parts.month}-${parts.day}`, time: `${parts.hour}:${parts.minute}` };
}

/** The first and last Colombo dates a service with this advance window may be asked for. */
export function requestWindow(today: string, minAdvanceDays: number, maxAdvanceDays: number): { first: string; last: string } {
  return { first: addDays(today, minAdvanceDays), last: addDays(today, maxAdvanceDays) };
}

/** Whole hours and the minutes left over, for "3 h" or "1 h 30 min". */
export function hoursAndMinutes(durationMinutes: number): { hours: number; minutes: number } {
  return { hours: Math.floor(durationMinutes / 60), minutes: durationMinutes % 60 };
}

function toMinutes(time: string): number {
  const [hours, minutes] = time.split(":").map(Number);
  return hours * 60 + minutes;
}

function fromMinutes(total: number): string {
  return `${String(Math.floor(total / 60)).padStart(2, "0")}:${String(total % 60).padStart(2, "0")}`;
}
