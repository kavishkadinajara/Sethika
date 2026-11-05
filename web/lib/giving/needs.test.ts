import { describe, expect, it } from "vitest";
import { needBar, needLeft, needPromised, needSteps } from "./needs";

const need = (quantityNeeded: number, quantityPledged: number, quantityReceived: number) => ({ quantityNeeded, quantityPledged, quantityReceived });

describe("a need's figures", () => {
  it("leaves what is not spoken for, in hundredths", () => {
    expect(needLeft(need(50, 20, 5))).toBe(30);
    expect(needLeft(need(0.3, 0.1, 0))).toBe(0.2);
    expect(needLeft(need(10, 10, 10))).toBe(0);
  });

  it("promises what is spoken for but has not arrived", () => {
    expect(needPromised(need(50, 20, 5))).toBe(15);
    expect(needPromised(need(50, 5, 5))).toBe(0);
  });

  it("draws arrived and promised side by side, never past the whole", () => {
    expect(needBar(need(50, 20, 5))).toEqual({ received: 10, promised: 30 });
    expect(needBar(need(10, 10, 10))).toEqual({ received: 100, promised: 0 });
    expect(needBar(need(0, 0, 0))).toEqual({ received: 0, promised: 0 });
  });
});

describe("a need's steps", () => {
  const steps = (status: "Draft" | "Open" | "Fulfilled" | "Closed" | "Cancelled", quantityPledged = 0, quantityReceived = 0) =>
    needSteps({ status, quantityPledged, quantityReceived });

  it("a draft opens, is edited or cancelled, and cannot close", () => {
    expect(steps("Draft")).toEqual({ edit: true, open: true, close: false, cancel: true });
  });

  it("an open need with promises waits for them", () => {
    expect(steps("Open", 5, 0)).toEqual({ edit: true, open: false, close: false, cancel: false });
  });

  it("an open need that received gifts closes, and is not cancelled", () => {
    expect(steps("Open", 5, 5)).toEqual({ edit: true, open: false, close: true, cancel: false });
  });

  it("a settled need takes no steps", () => {
    for (const status of ["Fulfilled", "Closed", "Cancelled"] as const) {
      expect(steps(status)).toEqual({ edit: false, open: false, close: false, cancel: false });
    }
  });
});
