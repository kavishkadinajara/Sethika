import type { SessionState } from "@/auth/session-client";
import { ANONYMOUS, createApiStub, DONOR } from "@/test-utils/api-stub";
import { institution } from "@/test-utils/fixtures";
import { json, press, render, settle, type } from "@/test-utils/render";
import { act } from "react";

import { GiveSheet } from "./give-sheet";

/*  Giving from the phone against the API, stubbed at the app's own client boundary, with PayHere's
    checkout standing in as the WebView's props: the page it would load, and the navigations it
    would ask about. PayHere's notification is the API's business; the sheet only reads it.     */

const mockApi = createApiStub();
let mockSession: SessionState = DONOR;
const mockWebView: { props: Record<string, unknown> | null } = { props: null };

jest.mock("@/api/http", () => ({ getJson: (path: string) => mockApi.getJson(path) }));
jest.mock("@/auth/session-provider", () => ({ useSession: () => mockSession, useSessionClient: () => mockApi.client }));
jest.mock("react-native-safe-area-context", () => jest.requireActual("react-native-safe-area-context/jest/mock").default);
jest.mock("react-native-webview", () => ({
  WebView: (props: Record<string, unknown>) => {
    mockWebView.props = props;
    return null;
  },
}));

const ORDER = "6f9619ff-8b86-d011-b42d-00cf4fc964ff";
const SLUG = "test-elders-home";
const PLACE = institution({ givesOnline: true });
const CHECKOUT = { orderId: ORDER, action: "https://sandbox.payhere.lk/pay/checkout", fields: { merchant_id: "1211149", hash: "H1" } };
const payment = (status: string, receiptNo: string | null = null) => ({
  orderId: ORDER,
  amount: 1500,
  currencyCode: "LKR",
  status,
  institutionName: "Test Elders' Home",
  institutionNameSi: null,
  institutionNameTa: null,
  institutionSlug: SLUG,
  receiptPublicId: receiptNo ? "r-1" : null,
  receiptNo,
});

async function fillAndSend(screen: Awaited<ReturnType<typeof render>>, amount = "1,500") {
  await type(screen.getTestId("gift-amount"), amount);
  await type(screen.getTestId("gift-email"), " donor@example.lk ");
  await type(screen.getTestId("gift-address"), "12 Temple Road");
  await type(screen.getTestId("gift-city"), "Peradeniya");
  await press(screen.getText("Go to PayHere"));
}

/** PayHere asking the WebView to load `url`; true means it stays in the checkout. */
async function navigate(url: string): Promise<boolean> {
  let stays = true;
  await act(async () => {
    stays = (mockWebView.props?.onShouldStartLoadWithRequest as (request: { url: string }) => boolean)({ url });
  });
  await settle();
  return stays;
}

beforeEach(() => {
  jest.useFakeTimers({ advanceTimers: true });
  mockApi.sent.length = 0;
  mockSession = DONOR;
  mockWebView.props = null;
  mockApi.serve({ [`POST /api/institutions/${SLUG}/give`]: () => json(201, CHECKOUT) });
});

afterEach(() => {
  jest.useRealTimers();
});

describe("before PayHere", () => {
  it("asks a visitor to sign in first, and goes where they choose", async () => {
    mockSession = ANONYMOUS;
    const onSignIn = jest.fn();
    const screen = await render(<GiveSheet institution={PLACE} onClose={jest.fn()} onSignIn={onSignIn} />);

    screen.getText("Sign in to give, so your receipt comes to you. You will come back here.");
    expect(screen.queryTestId("gift-amount")).toBeNull();
    await press(screen.getText("Create an account"));
    expect(onSignIn).toHaveBeenCalledWith("/sign-up");
  });

  it("says each missing or wrong detail and sends nothing", async () => {
    const screen = await render(<GiveSheet institution={PLACE} onClose={jest.fn()} onSignIn={jest.fn()} />);
    await type(screen.getTestId("gift-amount"), "50");
    await press(screen.getText("Go to PayHere"));

    screen.getText("Enter an amount from 100 to 1,000,000, with at most two decimals.");
    screen.getText("Enter a valid email address.");
    screen.getText("Enter your address.");
    screen.getText("Enter your city.");
    expect(mockApi.sent).toHaveLength(0);
  });

  it("marks the fields the API refused, and says a closed till in words", async () => {
    mockApi.serve({ [`POST /api/institutions/${SLUG}/give`]: () => json(400, { code: "invalid_details", fields: ["email"] }) });
    const screen = await render(<GiveSheet institution={PLACE} onClose={jest.fn()} onSignIn={jest.fn()} />);
    await fillAndSend(screen);
    screen.getText("Enter a valid email address.");
    expect(screen.queryText("Enter your city.")).toBeNull();

    mockApi.serve({ [`POST /api/institutions/${SLUG}/give`]: () => json(409, { code: "online_giving_off" }) });
    await press(screen.getText("Go to PayHere"));
    expect(screen.allText().join("\n")).toMatch(/online/i);
    expect(mockWebView.props).toBeNull();
  });
});

describe("PayHere and back", () => {
  it("sends the gift on the donor's session and opens PayHere's checkout with the signed form", async () => {
    const screen = await render(<GiveSheet institution={PLACE} onClose={jest.fn()} onSignIn={jest.fn()} />);
    await fillAndSend(screen);

    expect(mockApi.sent[0]).toMatchObject({
      method: "POST",
      path: `/api/institutions/${SLUG}/give`,
      signedIn: true,
      body: { amount: 1500, need: null, email: "donor@example.lk", address: "12 Temple Road", city: "Peradeniya" },
    });
    screen.getText("Pay on PayHere");
    const html = (mockWebView.props?.source as { html: string }).html;
    expect(html).toContain('action="https://sandbox.payhere.lk/pay/checkout"');
    expect(html).toContain('name="hash" value="H1"');
    // PayHere's own pages stay in the checkout.
    expect(await navigate("https://sandbox.payhere.lk/pay/o123")).toBe(true);
  });

  it("stops at Sethika's return address and waits for PayHere's notification, then thanks the donor", async () => {
    let answers = [payment("Pending"), payment("Paid", "RC-2026-000042")];
    mockApi.also({ [`GET /api/me/payments/${ORDER}`]: () => json(200, answers.shift() ?? payment("Paid", "RC-2026-000042")) });
    const screen = await render(<GiveSheet institution={PLACE} onClose={jest.fn()} onSignIn={jest.fn()} />);
    await fillAndSend(screen);

    expect(await navigate(`https://sethika.kavishkadinajara.com/giving/return/${ORDER}`)).toBe(false);
    screen.getText("Waiting for PayHere");

    await act(async () => {
      jest.advanceTimersByTime(3000);
    });
    await settle();
    screen.getText("Thank you");
    screen.getText("Test Elders' Home received your gift of LKR 1,500.");
    screen.getText("RC-2026-000042");
    answers = [];
  });

  it("never calls a payment the donor left paid", async () => {
    mockApi.also({ [`GET /api/me/payments/${ORDER}`]: () => json(200, payment("Pending")) });
    const screen = await render(<GiveSheet institution={PLACE} onClose={jest.fn()} onSignIn={jest.fn()} />);
    await fillAndSend(screen);

    expect(await navigate(`https://sethika.kavishkadinajara.com/si/giving/return/${ORDER}?left=1`)).toBe(false);
    screen.getText("The payment was not finished");
    expect(screen.queryText("Thank you")).toBeNull();
  });

  it("closing the checkout is leaving it, not paying", async () => {
    mockApi.also({ [`GET /api/me/payments/${ORDER}`]: () => json(200, payment("Pending")) });
    const onClose = jest.fn();
    const screen = await render(<GiveSheet institution={PLACE} onClose={onClose} onSignIn={jest.fn()} />);
    await fillAndSend(screen);

    await press(screen.getLabel("Close"));
    expect(onClose).not.toHaveBeenCalled();
    screen.getText("The payment was not finished");
    await press(screen.getText("Back to Test Elders' Home"));
    expect(onClose).toHaveBeenCalled();
  });
});
