import type { DonationListItem } from "./types";

/*  Figures and the month list's sums (plan 09, D.1). Pure, so unit-tested in node. */

/**
 * "5,000.00": always two decimals, digits grouped the English way. Intl with "en" rather than the
 * page's language: Sinhala and Tamil write the same digits, and Chromium has no Sinhala number data,
 * so the server and the browser would disagree (the hydration note in InstitutionPage.tsx).
 */
const figures = new Intl.NumberFormat("en", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

export function formatAmount(amount: number): string {
  return figures.format(amount);
}

/** A count of items: whole numbers as they are, parts with up to two decimals. */
const counts = new Intl.NumberFormat("en", { maximumFractionDigits: 2 });

export function formatQuantity(quantity: number): string {
  return counts.format(quantity);
}

/** `YYYY-MM` of a `YYYY-MM-DD` day. */
export function monthOf(day: string): string {
  return day.slice(0, 7);
}

/** The month before or after `YYYY-MM`. */
export function shiftMonth(month: string, by: number): string {
  const [year, m] = month.split("-").map(Number);
  const index = year * 12 + (m - 1) + by;
  return `${Math.floor(index / 12)}-${String((index % 12) + 1).padStart(2, "0")}`;
}

export type DonationFilter = { method: string | null; voided: "all" | "kept" | "voided" };

/** The rows a filter keeps: by payment method (items have none), and voided or not. */
export function filterDonations(rows: readonly DonationListItem[], filter: DonationFilter): DonationListItem[] {
  return rows.filter(
    (row) =>
      (filter.method === null || row.method === filter.method) &&
      (filter.voided === "all" || (filter.voided === "voided") === row.isVoided),
  );
}

/** Money received and gifts counted: voided gifts are out of every total (M6). */
export function totals(rows: readonly DonationListItem[]): { money: number; gifts: number; itemGifts: number; voided: number } {
  let money = 0;
  let gifts = 0;
  let itemGifts = 0;
  let voided = 0;
  for (const row of rows) {
    if (row.isVoided) {
      voided++;
      continue;
    }
    gifts++;
    if (row.kind === "Money") money += row.amount ?? 0;
    else itemGifts++;
  }
  // Sums of two-decimal figures drift in binary floating point (0.1 + 0.2); round back to cents.
  return { money: Math.round(money * 100) / 100, gifts, itemGifts, voided };
}

/**
 * Whether the person signed in may void the gift: not one already voided, not their own record
 * (maker-checker, M6), and only with the permission. The server checks all three again.
 */
export function canVoid(row: DonationListItem, holdsVoid: boolean): boolean {
  return holdsVoid && !row.isVoided && !row.recordedByMe;
}
