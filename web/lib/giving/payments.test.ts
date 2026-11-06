import { describe, expect, it } from "vitest";
import { parseGift, returnState } from "./payments";

describe("parseGift", () => {
  it("takes an amount within the limits, with or without commas and cents", () => {
    expect(parseGift("100")).toBe(100);
    expect(parseGift(" 2,500.50 ")).toBe(2500.5);
    expect(parseGift("1000000")).toBe(1_000_000);
    expect(parseGift("100.5")).toBe(100.5);
  });

  it("refuses what is outside the limits", () => {
    expect(parseGift("99.99")).toBeNull();
    expect(parseGift("1000000.01")).toBeNull();
  });

  it("refuses what is not an amount in whole cents", () => {
    expect(parseGift("")).toBeNull();
    expect(parseGift("abc")).toBeNull();
    expect(parseGift("100.001")).toBeNull();
    expect(parseGift("-500")).toBeNull();
    expect(parseGift("1e3")).toBeNull();
  });
});

describe("returnState", () => {
  it("believes the notification, whatever address the browser came back on", () => {
    expect(returnState("Paid", false)).toBe("paid");
    expect(returnState("Paid", true)).toBe("paid");
    expect(returnState("Failed", false)).toBe("failed");
    expect(returnState("Failed", true)).toBe("failed");
    expect(returnState("Cancelled", false)).toBe("left");
  });

  it("waits for the notification, unless the donor left", () => {
    expect(returnState("Pending", false)).toBe("waiting");
    expect(returnState("Pending", true)).toBe("left");
  });
});
