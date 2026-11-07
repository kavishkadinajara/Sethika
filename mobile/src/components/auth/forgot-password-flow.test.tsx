import { router } from "expo-router";

import { I18nProvider } from "@/i18n/i18n-provider";
import { createApiStub } from "@/test-utils/api-stub";
import { act } from "react";

import { json, press, render, settle, type, type Screen } from "@/test-utils/render";
import { ThemePreferenceProvider } from "@/theme/appearance";

import { ForgotPasswordFlow } from "./forgot-password-flow";

/*  F38 against the API's contract for /api/auth/otp/request, /otp/verify and /password/reset,
    stubbed at http.ts's postJson; the words a failure turns into are the real describeFailure's. */

const mockApi = createApiStub();
const mockSaved = jest.fn(async () => {});

jest.mock("@/api/http", () => ({
  ...jest.requireActual<typeof import("@/api/http")>("@/api/http"),
  postJson: (path: string, body: unknown) => mockApi.client.fetch(path, { method: "POST", body: JSON.stringify(body) }),
}));
jest.mock("expo-secure-store", () => ({ setItemAsync: mockSaved, getItemAsync: async () => null, deleteItemAsync: async () => {} }));
jest.mock("expo-router", () => ({ router: { push: jest.fn(), back: jest.fn(), dismissTo: jest.fn() } }));

const REQUEST = "POST /api/auth/otp/request";
const VERIFY = "POST /api/auth/otp/verify";
const RESET = "POST /api/auth/password/reset";

const PHONE = "077 123 4567";
const CODE = "123456";
const PASSWORD = "Lotus#2027Offering";

const NOT_ALLOWED = "Enter a Sri Lankan mobile number, for example 077 123 4567.";
const TOO_MANY = "Too many tries. Please wait a minute and try again.";
const NETWORK = "Can't reach Sethika right now. Check your connection and try again.";
const TICKET_GONE = "This password reset has expired or was already used. Please verify your number again.";

const page = (next?: string) => (
  <ThemePreferenceProvider>
    <I18nProvider>
      <ForgotPasswordFlow next={next} />
    </I18nProvider>
  </ThemePreferenceProvider>
);

const sentTo = (path: string) => mockApi.sent.filter((request) => request.path === path);
/** The banner above the form, as opposed to a message under a field. */
const alertOn = (screen: Screen) => screen.where((props) => props.accessibilityRole === "alert").length > 0;

async function toCode(screen: Screen) {
  await type(screen.getLabel("Mobile number"), PHONE);
  await press(screen.getText("Send code"));
}

async function toPassword(screen: Screen) {
  await toCode(screen);
  await type(screen.getLabel("6-digit code"), CODE);
  await press(screen.getText("Verify"));
}

/** The keyboard's key, which reaches the form without going through the button. */
async function keyboardKey(screen: Screen, label: string) {
  await act(async () => {
    screen.getLabel(label).props.onSubmitEditing();
  });
}

/** An answer the test releases by hand. */
function held() {
  let release: (response: Response) => void = () => {};
  const answer = new Promise<Response>((resolve) => {
    release = resolve;
  });
  return { answer, release };
}

async function submitPassword(screen: Screen, password = PASSWORD, confirmation = password) {
  await type(screen.getLabel("New password"), password);
  await type(screen.getLabel("Type the new password again"), confirmation);
  await press(screen.getText("Change password"));
}

beforeEach(() => {
  mockApi.sent.length = 0;
  mockSaved.mockClear();
  jest.mocked(router.dismissTo).mockClear();
  jest.mocked(router.back).mockClear();
  mockApi.serve({
    [REQUEST]: () => new Response(null, { status: 202 }),
    [VERIFY]: () => json(200, { ticket: "ticket-1" }),
    [RESET]: () => new Response(null, { status: 204 }),
  });
});

describe("the phone step", () => {
  it("asks for a reset code in the screen's language, and moves on", async () => {
    const screen = await render(page());
    screen.getText("Step 1 of 3");

    await toCode(screen);

    expect(sentTo("/api/auth/otp/request")[0].body).toEqual({ phoneNumber: PHONE, purpose: "reset", language: "en" });
    screen.getText("Enter the code");
    screen.getText("Step 2 of 3");
  });

  it("asks for a number before calling the API", async () => {
    const screen = await render(page());

    await press(screen.getText("Send code"));

    screen.getText("Enter your mobile number.");
    expect(mockApi.sent).toEqual([]);
  });

  it("puts a malformed number under the field, on the same step", async () => {
    mockApi.also({ [REQUEST]: () => json(400, { code: "invalid_phone_number" }) });
    const screen = await render(page());

    await toCode(screen);

    screen.getText(NOT_ALLOWED);
    screen.getText("Forgot your password?");
    expect(alertOn(screen)).toBe(false);
  });

  it("puts anything else above the form, on the same step", async () => {
    mockApi.also({ [REQUEST]: () => json(429, { code: "too_many_requests" }) });
    const screen = await render(page());

    await toCode(screen);

    screen.getText(TOO_MANY);
    screen.getText("Forgot your password?");
    expect(alertOn(screen)).toBe(true);
  });

  it("says so when the API cannot be reached", async () => {
    mockApi.also({
      [REQUEST]: () => {
        throw new TypeError("Network request failed");
      },
    });
    const screen = await render(page());

    await toCode(screen);

    screen.getText(NETWORK);
    screen.getText("Forgot your password?");
  });

  it("goes back to sign-in for a donor who remembered", async () => {
    const screen = await render(page());

    await press(screen.getText("Sign in"));

    expect(router.back).toHaveBeenCalledTimes(1);
  });
});

describe("old messages", () => {
  it("a field's message goes with the next try", async () => {
    mockApi.also({ [REQUEST]: () => json(400, { code: "invalid_phone_number" }) });
    const screen = await render(page());
    await toCode(screen);

    mockApi.also({ [REQUEST]: () => json(429, { code: "too_many_requests" }) });
    await press(screen.getText("Send code"));

    expect(screen.queryText(NOT_ALLOWED)).toBeNull();
  });

  it("the form's message goes with the next try", async () => {
    mockApi.also({ [REQUEST]: () => json(429, { code: "too_many_requests" }) });
    const screen = await render(page());
    await toCode(screen);

    mockApi.also({ [REQUEST]: () => json(400, { code: "invalid_phone_number" }) });
    await press(screen.getText("Send code"));

    expect(screen.queryText(TOO_MANY)).toBeNull();
  });

  it("a password's broken rules go with the next try", async () => {
    mockApi.also({ [RESET]: () => json(400, { code: "password_rejected", failures: ["TooShort"] }) });
    const screen = await render(page());
    await toPassword(screen);
    await submitPassword(screen, "short");

    mockApi.also({ [RESET]: () => json(429, { code: "too_many_requests" }) });
    await submitPassword(screen);

    expect(screen.queryText("• At least 6 characters")).toBeNull();
    expect(screen.queryText("Your password needs:")).toBeNull();
  });

  it("starting again leaves the last failure behind", async () => {
    mockApi.also({ [RESET]: () => json(429, { code: "too_many_requests" }) });
    const screen = await render(page());
    await toPassword(screen);
    await submitPassword(screen);

    await press(screen.getText("Start again"));

    expect(screen.queryText(TOO_MANY)).toBeNull();
  });
});

describe("the keyboard's key while a request is out", () => {
  it("sends the number once", async () => {
    const out = held();
    mockApi.also({ [REQUEST]: () => out.answer });
    const screen = await render(page());
    await type(screen.getLabel("Mobile number"), PHONE);

    await keyboardKey(screen, "Mobile number");
    await keyboardKey(screen, "Mobile number");
    out.release(new Response(null, { status: 202 }));
    await settle();

    expect(sentTo("/api/auth/otp/request")).toHaveLength(1);
  });

  it("spends one try of the code", async () => {
    const out = held();
    mockApi.also({ [VERIFY]: () => out.answer });
    const screen = await render(page());
    await toCode(screen);
    await type(screen.getLabel("6-digit code"), CODE);

    await keyboardKey(screen, "6-digit code");
    await keyboardKey(screen, "6-digit code");
    out.release(json(200, { ticket: "ticket-1" }));
    await settle();

    expect(sentTo("/api/auth/otp/verify")).toHaveLength(1);
  });

  it("spends the ticket once", async () => {
    const out = held();
    mockApi.also({ [RESET]: () => out.answer });
    const screen = await render(page());
    await toPassword(screen);
    await type(screen.getLabel("New password"), PASSWORD);
    await type(screen.getLabel("Type the new password again"), PASSWORD);

    await keyboardKey(screen, "Type the new password again");
    await keyboardKey(screen, "Type the new password again");
    out.release(new Response(null, { status: 204 }));
    await settle();

    expect(sentTo("/api/auth/password/reset")).toHaveLength(1);
  });
});

describe("whether the number has an account", () => {
  it("is never said: the code step says if, whatever the number", async () => {
    const screen = await render(page());

    await toCode(screen);

    screen.getText(`If ${PHONE} belongs to a Sethika account, a code is on its way. It can take a minute to arrive.`);
  });

  it("a number with no account fails the code like any wrong code", async () => {
    // The API keeps a decoy code for such a number, so a guess gets the ordinary answer.
    mockApi.also({ [VERIFY]: () => json(400, { code: "otp_incorrect", attemptsRemaining: 4 }) });
    const screen = await render(page());
    await toCode(screen);

    await type(screen.getLabel("6-digit code"), CODE);
    await press(screen.getText("Verify"));

    screen.getText("That code is not right. 4 tries left.");
    expect(screen.allText().join(" ")).not.toMatch(/no account|not registered|not found/i);
  });

  it("a decoy ticket fails like a spent one", async () => {
    // Reached without an account only by guessing a decoy code (PasswordResetHandler.VerifyAsync).
    mockApi.also({ [RESET]: () => json(400, { code: "reset_ticket_invalid" }) });
    const screen = await render(page());
    await toPassword(screen);

    await submitPassword(screen);

    screen.getText(TICKET_GONE);
  });
});

describe("the code step", () => {
  it("verifies the number that asked, with the reset purpose", async () => {
    const screen = await render(page());

    await toPassword(screen);

    expect(sentTo("/api/auth/otp/verify")[0].body).toEqual({ phoneNumber: PHONE, purpose: "reset", code: CODE });
    screen.getText("Choose a new password");
    screen.getText("Step 3 of 3");
  });

  it("wants all six digits before calling the API", async () => {
    const screen = await render(page());
    await toCode(screen);

    await type(screen.getLabel("6-digit code"), "123");
    await press(screen.getText("Verify"));

    expect(sentTo("/api/auth/otp/verify")).toEqual([]);
  });

  it("a wrong code stays here, under the field, and the field is emptied", async () => {
    mockApi.also({ [VERIFY]: () => json(400, { code: "otp_incorrect", attemptsRemaining: 2 }) });
    const screen = await render(page());
    await toCode(screen);

    await type(screen.getLabel("6-digit code"), CODE);
    await press(screen.getText("Verify"));

    screen.getText("That code is not right. 2 tries left.");
    screen.getText("Enter the code");
    expect(alertOn(screen)).toBe(false);
    expect(screen.getLabel("6-digit code").props.value).toBe("");
  });

  it("a 429 stays here, above the form, and the field is emptied", async () => {
    mockApi.also({ [VERIFY]: () => json(429, { code: "too_many_requests" }) });
    const screen = await render(page());
    await toCode(screen);

    await type(screen.getLabel("6-digit code"), CODE);
    await press(screen.getText("Verify"));

    screen.getText(TOO_MANY);
    screen.getText("Enter the code");
    expect(alertOn(screen)).toBe(true);
    expect(screen.getLabel("6-digit code").props.value).toBe("");
  });

  it("a new code empties the field and asks again for the same number", async () => {
    const screen = await render(page());
    await toCode(screen);
    await type(screen.getLabel("6-digit code"), "123");

    await press(screen.getText("Send a new code"));

    expect(sentTo("/api/auth/otp/request")).toHaveLength(2);
    expect(sentTo("/api/auth/otp/request")[1].body).toEqual({ phoneNumber: PHONE, purpose: "reset", language: "en" });
    expect(screen.getLabel("6-digit code").props.value).toBe("");
  });

  it("a refused new code stays here, above the form", async () => {
    const screen = await render(page());
    await toCode(screen);
    mockApi.also({ [REQUEST]: () => json(429, { code: "too_many_requests" }) });

    await press(screen.getText("Send a new code"));

    screen.getText(TOO_MANY);
    screen.getText("Enter the code");
    expect(alertOn(screen)).toBe(true);
  });

  it("changing the number goes back with the number still there", async () => {
    const screen = await render(page());
    await toCode(screen);

    await press(screen.getText("Change number"));

    expect(screen.getLabel("Mobile number").props.value).toBe(PHONE);
  });
});

describe("the password step", () => {
  it("checks the two fields before calling the API", async () => {
    const screen = await render(page());
    await toPassword(screen);

    await press(screen.getText("Change password"));
    screen.getText("Choose a new password.");
    screen.getText("Type the new password again.");

    await submitPassword(screen, PASSWORD, "Lotus#2028Offering");
    screen.getText("The two passwords don't match.");

    expect(sentTo("/api/auth/password/reset")).toEqual([]);
  });

  it("a refused password stays here, says which rules, and keeps the ticket", async () => {
    mockApi.also({ [RESET]: () => json(400, { code: "password_rejected", failures: ["TooShort", "MissingDigit"] }) });
    const screen = await render(page());
    await toPassword(screen);

    await submitPassword(screen, "short");

    screen.getText("Please choose a stronger password.");
    screen.getText("• At least 6 characters");
    screen.getText("• A number (0–9)");
    screen.getText("Choose a new password");

    mockApi.also({ [RESET]: () => new Response(null, { status: 204 }) });
    await submitPassword(screen);

    expect(sentTo("/api/auth/password/reset")[1].body).toEqual({ ticket: "ticket-1", newPassword: PASSWORD });
    expect(sentTo("/api/auth/otp/verify")).toHaveLength(1);
    expect(router.dismissTo).toHaveBeenCalledTimes(1);
  });

  it("an invalid ticket starts again from the number, and the old ticket is never sent again", async () => {
    mockApi.also({ [RESET]: () => json(400, { code: "reset_ticket_invalid" }) });
    const screen = await render(page());
    await toPassword(screen);

    await submitPassword(screen);

    screen.getText(TICKET_GONE);
    screen.getText("Forgot your password?");
    expect(alertOn(screen)).toBe(true);
    expect(screen.getLabel("Mobile number").props.value).toBe(PHONE);

    mockApi.also({
      [VERIFY]: () => json(200, { ticket: "ticket-2" }),
      [RESET]: () => new Response(null, { status: 204 }),
    });
    await press(screen.getText("Send code"));
    await type(screen.getLabel("6-digit code"), CODE);
    await press(screen.getText("Verify"));
    await submitPassword(screen);

    expect(sentTo("/api/auth/password/reset")[1].body).toEqual({ ticket: "ticket-2", newPassword: PASSWORD });
  });

  it.each([
    ["a 429", () => json(429, { code: "too_many_requests" }), TOO_MANY],
    [
      "a lost connection",
      () => {
        throw new TypeError("Network request failed");
      },
      NETWORK,
    ],
  ])("%s stays here with the ticket, and the same press tries again", async (_name, answer, words) => {
    mockApi.also({ [RESET]: answer });
    const screen = await render(page());
    await toPassword(screen);

    await submitPassword(screen);

    screen.getText(words);
    screen.getText("Choose a new password");
    expect(screen.getLabel("New password").props.value).toBe(PASSWORD);

    mockApi.also({ [RESET]: () => new Response(null, { status: 204 }) });
    await press(screen.getText("Change password"));

    expect(sentTo("/api/auth/password/reset")[1].body).toEqual({ ticket: "ticket-1", newPassword: PASSWORD });
  });

  it("starting again leaves the password step for the number", async () => {
    const screen = await render(page());
    await toPassword(screen);

    await press(screen.getText("Start again"));

    screen.getText("Forgot your password?");
    expect(sentTo("/api/auth/password/reset")).toEqual([]);
  });
});

describe("a finished reset", () => {
  it("goes back to sign-in with the message, and starts no session", async () => {
    const screen = await render(page());
    await toPassword(screen);

    await submitPassword(screen);

    expect(sentTo("/api/auth/password/reset")[0].body).toEqual({ ticket: "ticket-1", newPassword: PASSWORD });
    expect(router.dismissTo).toHaveBeenCalledWith({ pathname: "/sign-in", params: { reset: "1" } });
    expect(mockApi.sent.map((request) => request.path).filter((path) => path.includes("/mobile/"))).toEqual([]);
  });

  it("hands on where the donor was going", async () => {
    const screen = await render(page("/i/kandy-temple?slot=7&date=2026-11-30"));
    await toPassword(screen);

    await submitPassword(screen);

    expect(router.dismissTo).toHaveBeenCalledWith({
      pathname: "/sign-in",
      params: { reset: "1", next: "/i/kandy-temple?slot=7&date=2026-11-30" },
    });
  });

  it("never puts the ticket in the store", async () => {
    const screen = await render(page());
    await toPassword(screen);

    await submitPassword(screen);

    expect(mockSaved).not.toHaveBeenCalled();
  });
});
