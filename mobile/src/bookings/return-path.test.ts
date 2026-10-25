import { LUNCH } from "@/test-utils/fixtures";

import { bookingReturnPath, chosenDay, safeReturnPath } from "./return-path";

/*  The way back to the same meal and day after signing in (plan 03, E33). */

describe("bookingReturnPath", () => {
  it("names the institution by slug and the day by the slot's public id and date", () => {
    expect(bookingReturnPath("test-elders-home", LUNCH, "2026-11-20")).toBe(`/i/test-elders-home?slot=${LUNCH}&date=2026-11-20`);
  });

  it("reads back as the same meal and day", () => {
    const path = bookingReturnPath("test-elders-home", LUNCH, "2026-11-20");
    const query = new URLSearchParams(path.split("?")[1]);
    expect(chosenDay({ slot: query.get("slot"), date: query.get("date") })).toEqual({ slot: LUNCH, date: "2026-11-20" });
    expect(safeReturnPath(path)).toBe(path);
  });
});

describe("safeReturnPath", () => {
  it("accepts only an institution page of this app", () => {
    expect(safeReturnPath("/i/test-elders-home")).toBe("/i/test-elders-home");
    expect(safeReturnPath("//evil.example/i/x")).toBeNull();
    expect(safeReturnPath("https://evil.example/i/x")).toBeNull();
    expect(safeReturnPath("/profile")).toBeNull();
    expect(safeReturnPath("/i/../profile")).toBeNull();
    expect(safeReturnPath(["/i/a"])).toBeNull();
    expect(safeReturnPath(undefined)).toBeNull();
  });
});

describe("chosenDay", () => {
  it("needs both a slot id and a real date", () => {
    expect(chosenDay({ slot: LUNCH, date: "2026-02-30" })).toBeNull();
    expect(chosenDay({ slot: "42", date: "2026-11-20" })).toBeNull();
    expect(chosenDay({ slot: LUNCH })).toBeNull();
    expect(chosenDay({})).toBeNull();
  });
});
