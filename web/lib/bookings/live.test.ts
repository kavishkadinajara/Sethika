import { describe, expect, it } from "vitest";
import { touchesMonth } from "./live";

describe("touchesMonth", () => {
  it("is true for a change of every day", () => {
    expect(touchesMonth({ from: null, to: null }, "2026-11")).toBe(true);
  });

  it("is true for a day inside the month, and its first and last days", () => {
    expect(touchesMonth({ from: "2026-11-14", to: "2026-11-14" }, "2026-11")).toBe(true);
    expect(touchesMonth({ from: "2026-11-01", to: "2026-11-01" }, "2026-11")).toBe(true);
    expect(touchesMonth({ from: "2026-11-30", to: "2026-11-30" }, "2026-11")).toBe(true);
  });

  it("is true for a range that starts before the month and ends in it, or spans it", () => {
    expect(touchesMonth({ from: "2026-10-28", to: "2026-11-02" }, "2026-11")).toBe(true);
    expect(touchesMonth({ from: "2026-10-01", to: "2026-12-31" }, "2026-11")).toBe(true);
  });

  it("is false for days before or after the month", () => {
    expect(touchesMonth({ from: "2026-10-31", to: "2026-10-31" }, "2026-11")).toBe(false);
    expect(touchesMonth({ from: "2026-12-01", to: "2026-12-03" }, "2026-11")).toBe(false);
  });
});
