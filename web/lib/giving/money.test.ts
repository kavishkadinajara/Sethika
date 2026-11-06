import { describe, expect, it } from "vitest";
import { canVoid, filterDonations, formatAmount, formatQuantity, monthOf, shiftMonth, totals } from "./money";
import type { DonationListItem } from "./types";

const row = (over: Partial<DonationListItem>): DonationListItem => ({
  publicId: "p",
  receiptNo: "RCP-2026-000001",
  kind: "Money",
  amount: 100,
  method: "CASH",
  items: null,
  quantity: null,
  donatedAt: "2026-11-04T05:00:00Z",
  donorName: "Sunil",
  fromDonorBook: false,
  recordedByName: "Staff",
  recordedByMe: false,
  isVoided: false,
  voidedAt: null,
  version: "AAAAAAAAB9E=",
  ...over,
});

describe("formatAmount", () => {
  it("always shows two decimals and groups thousands", () => {
    expect(formatAmount(5000)).toBe("5,000.00");
    expect(formatAmount(1234567.5)).toBe("1,234,567.50");
    expect(formatAmount(0.1)).toBe("0.10");
  });

  it("shows counts of items without needless decimals", () => {
    expect(formatQuantity(50)).toBe("50");
    expect(formatQuantity(2.5)).toBe("2.5");
  });
});

describe("months", () => {
  it("reads the month of a day and steps across years", () => {
    expect(monthOf("2026-11-04")).toBe("2026-11");
    expect(shiftMonth("2026-01", -1)).toBe("2025-12");
    expect(shiftMonth("2026-12", 1)).toBe("2027-01");
    expect(shiftMonth("2026-11", 0)).toBe("2026-11");
  });
});

describe("the month list", () => {
  const rows = [
    row({ publicId: "a", amount: 0.1 }),
    row({ publicId: "b", amount: 0.2, method: "BANK_TRANSFER" }),
    row({ publicId: "c", kind: "InKind", amount: null, method: null, items: "Rice" }),
    row({ publicId: "d", amount: 1000, isVoided: true }),
  ];

  it("leaves voided gifts out of every total, and rounds to cents", () => {
    expect(totals(rows)).toEqual({ money: 0.3, gifts: 3, itemGifts: 1, voided: 1 });
  });

  it("filters by method and by voided", () => {
    expect(filterDonations(rows, { method: "CASH", voided: "all" }).map((r) => r.publicId)).toEqual(["a", "d"]);
    expect(filterDonations(rows, { method: null, voided: "voided" }).map((r) => r.publicId)).toEqual(["d"]);
    expect(filterDonations(rows, { method: null, voided: "kept" }).map((r) => r.publicId)).toEqual(["a", "b", "c"]);
  });

  it("offers the void to a second person only", () => {
    expect(canVoid(row({}), true)).toBe(true);
    expect(canVoid(row({ recordedByMe: true }), true)).toBe(false);
    expect(canVoid(row({ isVoided: true }), true)).toBe(false);
    expect(canVoid(row({}), false)).toBe(false);
  });
});
