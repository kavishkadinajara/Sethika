import type { BookingFailure } from "@/bookings/errors";
import { bookingErrorKey } from "@/bookings/errors";
import { hasMessage, type MessageKey } from "@/i18n/translate";

import type { CheckoutForm, PaymentStatus } from "./types";

/*  Online giving on the phone (plan 09, D.3; PHASE-F-MOBILE §5.8). Pure, so unit-tested in node.
    The amount rules and the return states are the web's (web/lib/giving/payments.ts); the server
    has the last word on all of them. What is the phone's own: PayHere's checkout is a web page
    the app shows in a WebView, and the app reads the moment PayHere sends the donor back.     */

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
 * What the result says. Only `status` comes from PayHere's signed notification; `left` is how the
 * donor came back (PayHere's cancel address, or closing the checkout), so it can only soften
 * "waiting", never claim anything was paid (M9).
 */
export function returnState(status: PaymentStatus, left: boolean): ReturnState {
  if (status === "Paid") return "paid";
  if (status === "Failed") return "failed";
  if (status === "Cancelled") return "left";
  return left ? "left" : "waiting";
}

/** How many times the result asks again while the notification is on its way, three seconds apart. */
export const RETURN_CHECKS = 10;
export const RETURN_CHECK_MS = 3000;

/** PayHere's two checkouts (ConfiguredPayHereSite.cs). The app posts to nothing else. */
const CHECKOUTS = ["https://sandbox.payhere.lk/pay/checkout", "https://www.payhere.lk/pay/checkout"];

export function isPayHereCheckout(action: string): boolean {
  return CHECKOUTS.includes(action);
}

const escape = (value: string) =>
  value.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;").replace(/'/g, "&#39;");

/**
 * A page that posts the API's signed form to PayHere as soon as it loads: what the web does with a
 * hidden form, for the WebView. Every value is escaped, and only PayHere's own checkout is accepted
 * as the target, so nothing in the answer can turn the page into something else.
 */
export function checkoutHtml(form: CheckoutForm): string | null {
  if (!isPayHereCheckout(form.action)) return null;
  const inputs = Object.entries(form.fields)
    .map(([name, value]) => `<input type="hidden" name="${escape(name)}" value="${escape(value)}">`)
    .join("");
  return (
    `<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"></head>` +
    `<body><form id="pay" method="POST" action="${escape(form.action)}">${inputs}</form>` +
    `<script>document.getElementById("pay").submit();</script></body></html>`
  );
}

/**
 * Whether `url` is PayHere sending the donor back for `orderId`: its return address
 * (…/giving/return/{orderId}) or its cancel address (the same with ?left=1), in any language
 * (IPayHereSite). Anything else, PayHere's own pages included, stays in the checkout.
 */
export function payHereReturn(url: string, orderId: string): { left: boolean } | null {
  const match = /^https?:\/\/[^/?#]+(?:\/(?:si|ta|en))?\/giving\/return\/([0-9a-fA-F-]{36})\/?(\?[^#]*)?(#.*)?$/.exec(url);
  if (!match || match[1].toLowerCase() !== orderId.toLowerCase()) return null;
  return { left: /(^\?|&)left=1(&|$)/.test(match[2] ?? "") };
}

/** The giving codes with their own sentence (DonationErrors, plan 09), as on the web. */
const GIVING_CODES = ["invalid_details", "need_not_found", "need_closed", "online_giving_off", "payment_not_found", "phone_required"] as const;

/** The message that explains a refused gift; anything else is said as a booking failure is (session, network). */
export function givingErrorKey(failure: BookingFailure): MessageKey {
  if (failure.code && (GIVING_CODES as readonly string[]).includes(failure.code)) {
    const key = `Giving.errors.${failure.code}`;
    if (hasMessage(key)) return key;
  }
  if (failure.status === 403 && failure.code === null) return "Giving.give.notAllowed";
  return bookingErrorKey(failure);
}
