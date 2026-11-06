import type { PaymentStatus } from "./types";

/*  Online giving (plan 09, D.3). Pure, so unit-tested in node. The server has the last word on all of this. */

/** The limits of `CK_OnlinePay_Amount`. */
export const MIN_GIFT = 100;
export const MAX_GIFT = 1_000_000;

/** What the donor typed, as a figure, or null when it is not an amount that can be given online (M8). */
export function parseGift(text: string): number | null {
  const tidy = text.trim().replaceAll(",", "");
  if (!/^\d+(\.\d{1,2})?$/.test(tidy)) return null;
  const amount = Number(tidy);
  return amount >= MIN_GIFT && amount <= MAX_GIFT ? amount : null;
}

export type ReturnState = "paid" | "waiting" | "failed" | "left";

/**
 * What the return page says. Only `status` comes from PayHere's signed notification; `left` is the
 * address the browser came back on, so it can only soften "waiting", never claim anything was paid (M9).
 */
export function returnState(status: PaymentStatus, left: boolean): ReturnState {
  if (status === "Paid") return "paid";
  if (status === "Failed") return "failed";
  if (status === "Cancelled") return "left";
  return left ? "left" : "waiting";
}

/** How many times the return page asks again while the notification is on its way, three seconds apart. */
export const RETURN_CHECKS = 10;
export const RETURN_CHECK_MS = 3000;
