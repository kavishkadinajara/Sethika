import type { Sponsorship, SponsorshipDay, SponsorshipInput, StandingPattern } from "./types";

/** What the pattern picker holds: every pattern's fields at once, so switching back keeps a choice. */
export type PatternForm = {
  pattern: StandingPattern;
  /** 0 Sunday ... 6 Saturday. */
  weekDays: number[];
  /** 1 ... 31. */
  monthDays: number[];
  /** Every this many months, from the start month (1 ... 12). */
  monthInterval: number;
  yearMonth: number;
  yearDay: number;
  startDate: string;
  /** "" for no end. */
  endDate: string;
};

export type PatternFields = Pick<SponsorshipInput, "pattern" | "monthInterval" | "weekDays" | "monthDays" | "yearMonth" | "yearDay" | "startDate" | "endDate">;

/**
 * The fields the API takes for the chosen pattern, and no others: it refuses a field sent for another
 * pattern rather than dropping it (CK_Standing_Shape, K15). A `Once` has no end of its own.
 */
export function patternFields(form: PatternForm): PatternFields {
  const fields: PatternFields = { pattern: form.pattern, startDate: form.startDate };
  if (form.pattern !== "Once") fields.endDate = form.endDate || null;

  switch (form.pattern) {
    case "Weekly":
      fields.weekDays = [...form.weekDays].sort((a, b) => a - b);
      break;
    case "Monthly":
      fields.monthDays = [...form.monthDays].sort((a, b) => a - b);
      fields.monthInterval = form.monthInterval;
      break;
    case "Yearly":
      fields.yearMonth = form.yearMonth;
      fields.yearDay = form.yearDay;
      break;
  }
  return fields;
}

/** The days a month has in a leap year: 29 February is a day of some year, 31 April never is (K15). */
export function isDayOfSomeYear(month: number, day: number): boolean {
  return Number.isInteger(month) && Number.isInteger(day) && month >= 1 && month <= 12 && day >= 1 && day <= new Date(Date.UTC(2000, month, 0)).getUTCDate();
}

/** The pattern fields to fix before the server is asked: the same names the API's `fields` uses. */
export function patternProblems(form: PatternForm, today: string): string[] {
  const problems: string[] = [];
  if (form.pattern === "Weekly" && form.weekDays.length === 0) problems.push("weekDays");
  if (form.pattern === "Monthly" && form.monthDays.length === 0) problems.push("monthDays");
  if (form.pattern === "Yearly" && !isDayOfSomeYear(form.yearMonth, form.yearDay)) problems.push("yearDay");
  if (!/^\d{4}-\d{2}-\d{2}$/.test(form.startDate) || form.startDate < today) problems.push("startDate");
  else if (form.pattern !== "Once" && form.endDate && form.endDate < form.startDate) problems.push("endDate");
  return problems;
}

/** Each donor's sponsorships: the active ones first, each group by start date. */
export function byDonor(sponsorships: readonly Sponsorship[]): Map<string, Sponsorship[]> {
  const groups = new Map<string, Sponsorship[]>();
  for (const sponsorship of sponsorships) groups.set(sponsorship.donor, [...(groups.get(sponsorship.donor) ?? []), sponsorship]);
  for (const group of groups.values()) {
    group.sort((a, b) => Number(b.status === "Active") - Number(a.status === "Active") || a.startDate.localeCompare(b.startDate));
  }
  return groups;
}

/**
 * Whether a regular donor can let one of their own days go from My bookings (K24, K35): a regular
 * day, still booked, today or later. Today too, unlike an ordinary booking's cancel (plan 03 Q1).
 */
export function canLetGoOwnDay(booking: { regular: boolean; status: string; date: string }, today: string): boolean {
  return booking.regular && (booking.status === "Requested" || booking.status === "Approved") && booking.date >= today;
}

/**
 * Whether staff can still say the sponsor cannot give on a day (K23): today or later, not already let
 * go or moved, and not a meal already given.
 */
export function canSkip(day: SponsorshipDay, today: string): boolean {
  if (day.date < today) return false;
  if (day.state === "booked") return day.bookingStatus !== "Completed" && day.bookingStatus !== "NoShow";
  return day.state === "open" || day.state === "upcoming" || day.state === "cancelled";
}

/**
 * The days a swap can take from a sponsorship (K29): this time, a booked day from today on; from now
 * on, any day of the pattern from today on that has not been let go or moved.
 */
export function swappableDays(days: readonly SponsorshipDay[], today: string, permanent: boolean): SponsorshipDay[] {
  return days.filter(
    (day) => day.date >= today && (day.state === "booked" ? day.bookingStatus === "Approved" || day.bookingStatus === "Requested" : permanent && (day.state === "open" || day.state === "upcoming")),
  );
}

/** Who a sponsorship can swap with: another active one of the same meal. */
export function swapPartners(all: readonly Sponsorship[], own: Sponsorship): Sponsorship[] {
  return all.filter((s) => s.publicId !== own.publicId && s.status === "Active" && s.mealSlot === own.mealSlot);
}
