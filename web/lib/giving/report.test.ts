import { describe, expect, it } from "vitest";
import { csvName, csvPath, openPledges, recentGifts } from "./report";
import type { MyDonation, MyPledge } from "./types";

const gift = (receiptNo: string, isVoided = false) => ({ receiptNo, isVoided }) as MyDonation;
const pledge = (publicId: string, status: MyPledge["status"], expectedDate: string | null) => ({ publicId, status, expectedDate }) as MyPledge;

describe("csvPath", () => {
  it("names the month and the language", () => {
    expect(csvPath("/api/institution/reports/monthly/csv", "2026-11", "si")).toBe("/api/institution/reports/monthly/csv?month=2026-11&lang=si");
  });
});

describe("csvName", () => {
  it("says what it is and which month", () => {
    expect(csvName("report", "2026-11")).toBe("sethika-report-2026-11.csv");
    expect(csvName("donations", "2026-10")).toBe("sethika-donations-2026-10.csv");
  });
});

describe("recentGifts", () => {
  it("keeps the order, leaves voided gifts out, and stops at the number shown", () => {
    const rows = [gift("A"), gift("B", true), gift("C"), gift("D")];
    expect(recentGifts(rows, 2).map((r) => r.receiptNo)).toEqual(["A", "C"]);
  });
});

describe("openPledges", () => {
  it("keeps only what is still owed, soonest first, undated last", () => {
    const rows = [
      pledge("undated", "Pledged", null),
      pledge("later", "Pledged", "2026-12-01"),
      pledge("done", "Delivered", "2026-11-01"),
      pledge("off", "Cancelled", "2026-11-02"),
      pledge("soon", "Pledged", "2026-11-10"),
    ];
    expect(openPledges(rows, 5).map((r) => r.publicId)).toEqual(["soon", "later", "undated"]);
  });

  it("stops at the number shown, and leaves the list it was given alone", () => {
    const rows = [pledge("b", "Pledged", "2026-12-01"), pledge("a", "Pledged", "2026-11-01")];
    expect(openPledges(rows, 1).map((r) => r.publicId)).toEqual(["a"]);
    expect(rows.map((r) => r.publicId)).toEqual(["b", "a"]);
  });
});
