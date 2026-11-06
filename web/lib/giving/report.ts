import type { MyDonation, MyPledge } from "./types";

/*  What the report and the dashboards work out for themselves (plan 09, D.4). Pure, so unit-tested in
    node. The figures themselves come from the API.                                                */

/** The file's address: the month, and the language the names in it are written in. */
export function csvPath(path: string, month: string, locale: string): string {
  return `${path}?month=${encodeURIComponent(month)}&lang=${encodeURIComponent(locale)}`;
}

/** "sethika-report-2026-11.csv": what the browser saves it as. */
export function csvName(kind: "report" | "donations", month: string): string {
  return `sethika-${kind}-${month}.csv`;
}

/** Gifts the donor's home shows: the newest that still stand. A voided gift waits in My giving (M18). */
export function recentGifts(rows: readonly MyDonation[], shown: number): MyDonation[] {
  return rows.filter((row) => !row.isVoided).slice(0, shown);
}

/** Pledges still owed, the soonest expected first and those with no date last. */
export function openPledges(rows: readonly MyPledge[], shown: number): MyPledge[] {
  return rows
    .filter((row) => row.status === "Pledged")
    .sort((a, b) => (a.expectedDate ?? "9999").localeCompare(b.expectedDate ?? "9999"))
    .slice(0, shown);
}
