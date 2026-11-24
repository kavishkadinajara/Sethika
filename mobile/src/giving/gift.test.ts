import { createTranslator } from "@/i18n/translate";

import { checkoutHtml, givingErrorKey, isPayHereCheckout, parseGift, payHereReturn, returnState } from "./gift";
import type { CheckoutForm } from "./types";

const ORDER = "6f9619ff-8b86-d011-b42d-00cf4fc964ff";
const form = (overrides: Partial<CheckoutForm> = {}): CheckoutForm => ({
  orderId: ORDER,
  action: "https://sandbox.payhere.lk/pay/checkout",
  fields: { merchant_id: "1211149", order_id: ORDER, amount: "1500.00", hash: "ABC123", items: "Gift to Test Elders' Home" },
  ...overrides,
});

describe("parseGift (as the web, M8)", () => {
  it("takes LKR 100 to 1,000,000 with at most two decimals, commas allowed", () => {
    expect(parseGift("100")).toBe(100);
    expect(parseGift(" 1,500.50 ")).toBe(1500.5);
    expect(parseGift("1000000")).toBe(1_000_000);
  });

  it("refuses what cannot be given online", () => {
    for (const text of ["", "99.99", "1000000.01", "12.345", "-500", "1e3", "abc", "500 LKR"]) expect(parseGift(text)).toBeNull();
  });
});

describe("returnState (M9)", () => {
  it("believes only the signed notification about money", () => {
    expect(returnState("Paid", true)).toBe("paid");
    expect(returnState("Failed", false)).toBe("failed");
    expect(returnState("Cancelled", false)).toBe("left");
  });

  it("lets the way back soften waiting, never turn it into paid", () => {
    expect(returnState("Pending", false)).toBe("waiting");
    expect(returnState("Pending", true)).toBe("left");
  });
});

describe("checkoutHtml", () => {
  it("posts every signed field to PayHere as the page loads", () => {
    const html = checkoutHtml(form())!;
    expect(html).toContain('<form id="pay" method="POST" action="https://sandbox.payhere.lk/pay/checkout">');
    expect(html).toContain('<input type="hidden" name="merchant_id" value="1211149">');
    expect(html).toContain('<input type="hidden" name="hash" value="ABC123">');
    expect(html).toContain('document.getElementById("pay").submit()');
  });

  it("escapes every value, so an answer cannot add markup or script", () => {
    const html = checkoutHtml(form({ fields: { items: `Gift"><script>alert(1)</script>&'` } }))!;
    expect(html).toContain('value="Gift&quot;&gt;&lt;script&gt;alert(1)&lt;/script&gt;&amp;&#39;"');
    // Counted in any letter case and with any attributes: only the page's own submit script.
    expect(html.toLowerCase().split("<script").length - 1).toBe(1);
  });

  it("posts only to PayHere's own checkouts", () => {
    expect(isPayHereCheckout("https://www.payhere.lk/pay/checkout")).toBe(true);
    expect(checkoutHtml(form({ action: "https://evil.example/pay/checkout" }))).toBeNull();
    expect(checkoutHtml(form({ action: "http://sandbox.payhere.lk/pay/checkout" }))).toBeNull();
  });
});

describe("payHereReturn", () => {
  const web = "https://sethika.kavishkadinajara.com";

  it("knows PayHere sending the donor back, in any language", () => {
    expect(payHereReturn(`${web}/giving/return/${ORDER}`, ORDER)).toEqual({ left: false });
    expect(payHereReturn(`${web}/si/giving/return/${ORDER}`, ORDER)).toEqual({ left: false });
    expect(payHereReturn(`${web}/ta/giving/return/${ORDER.toUpperCase()}?left=1`, ORDER)).toEqual({ left: true });
  });

  it("leaves everything else in the checkout", () => {
    expect(payHereReturn("https://sandbox.payhere.lk/pay/checkout", ORDER)).toBeNull();
    expect(payHereReturn("https://sandbox.payhere.lk/pay/o12345", ORDER)).toBeNull();
    expect(payHereReturn(`${web}/giving/return/00000000-0000-0000-0000-000000000000`, ORDER)).toBeNull();
    expect(payHereReturn(`${web}/giving/payments`, ORDER)).toBeNull();
    expect(payHereReturn("about:blank", ORDER)).toBeNull();
  });
});

describe("givingErrorKey", () => {
  const say = (status: number, code: string | null) => createTranslator("en")(givingErrorKey({ status, code, fields: [] }));

  it("says the giving refusals in their own words", () => {
    expect(givingErrorKey({ status: 409, code: "online_giving_off", fields: [] })).toBe("Giving.errors.online_giving_off");
    expect(givingErrorKey({ status: 404, code: "payment_not_found", fields: [] })).toBe("Giving.errors.payment_not_found");
    expect(say(409, "online_giving_off")).not.toBe(say(500, null));
  });

  it("an account that may not give, the network and the rest as the booking screens say them", () => {
    expect(givingErrorKey({ status: 403, code: null, fields: [] })).toBe("Giving.give.notAllowed");
    expect(givingErrorKey({ status: 0, code: null, fields: [] })).toBe("Errors.network");
    expect(givingErrorKey({ status: 429, code: null, fields: [] })).toBe("Errors.tooManyRequestsNoTime");
    expect(givingErrorKey({ status: 500, code: null, fields: [] })).toBe("Errors.unexpected");
  });
});
