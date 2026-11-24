import * as Notifications from "expo-notifications";
import { router } from "expo-router";

import type { SessionState } from "@/auth/session-client";
import { ANONYMOUS, createApiStub, DONOR } from "@/test-utils/api-stub";
import { createPushStub, permissions } from "@/test-utils/push-stub";
import { render } from "@/test-utils/render";

import { PushProvider, usePush } from "./push-provider";

/*  When registration runs and where a tap goes. The provider is real, over a scripted phone and
    the app's API boundary; Notifications stands in only for the hook that reports a tap.       */

const mockApi = createApiStub();
const mockPhone = createPushStub();
let mockSession: SessionState = DONOR;
let mockLanguageName = "Bookings";
let mockTap: Notifications.NotificationResponse | null | undefined = null;
const mockClient = { ...mockApi.client, getState: () => mockSession };

jest.mock("@/auth/session-provider", () => ({
  useSession: () => mockSession,
  useSessionClient: () => mockClient,
}));
jest.mock("@/i18n/i18n-provider", () => ({ useI18n: () => ({ t: () => mockLanguageName }) }));
jest.mock("@/notifications/native-device", () => ({
  get nativeDevice() {
    return mockPhone.device;
  },
  get nativeMemory() {
    return mockPhone.memory;
  },
}));
jest.mock("expo-router", () => ({ router: { navigate: jest.fn() } }));
jest.mock("expo-notifications", () => ({
  DEFAULT_ACTION_IDENTIFIER: "default-tap",
  useLastNotificationResponse: () => mockTap,
  clearLastNotificationResponseAsync: jest.fn(async () => {}),
}));

function tap(code: unknown, actionIdentifier = "default-tap") {
  return {
    actionIdentifier,
    notification: { request: { content: { data: { code } } } },
  } as unknown as Notifications.NotificationResponse;
}

const page = () => <PushProvider>{null}</PushProvider>;
const registrations = () => mockApi.sent.filter((request) => request.path === "/api/me/devices");

beforeEach(() => {
  mockApi.sent.length = 0;
  mockApi.serve({ "PUT /api/me/devices": () => new Response(null, { status: 204 }) });
  mockPhone.events.length = 0;
  mockPhone.stored.clear();
  mockPhone.phone.permission = permissions.GRANTED;
  mockPhone.device.supported = true;
  mockSession = DONOR;
  mockLanguageName = "Bookings";
  mockTap = null;
  jest.mocked(router.navigate).mockClear();
  jest.mocked(Notifications.clearLastNotificationResponseAsync).mockClear();
});

describe("registration", () => {
  it("runs for a signed-in session, with the channel's name in the app's language", async () => {
    mockLanguageName = "වෙන්කිරීම්";

    await render(page());

    expect(mockPhone.events[0]).toBe("channel:වෙන්කිරීම්");
    expect(registrations()).toHaveLength(1);
  });

  it("waits for a sign-in: a visitor is never asked", async () => {
    mockSession = ANONYMOUS;

    const screen = await render(page());
    expect(mockPhone.events).toEqual([]);

    mockSession = DONOR;
    await screen.rerender(page());
    expect(registrations()).toHaveLength(1);
  });

  it("does not run again when the session only rotates", async () => {
    const screen = await render(page());

    mockSession = { ...DONOR, accessToken: "rotated" } as SessionState;
    await screen.rerender(page());

    expect(registrations()).toHaveLength(1);
  });
});

describe("a tap", () => {
  it("opens My bookings and is cleared, so it cannot open it twice", async () => {
    mockTap = tap("BOOKING_REMINDER");

    await render(page());

    expect(router.navigate).toHaveBeenCalledTimes(1);
    expect(router.navigate).toHaveBeenCalledWith("/bookings");
    expect(Notifications.clearLastNotificationResponseAsync).toHaveBeenCalledTimes(1);
  });

  it("works for a signed-out donor too: the screen asks them to sign in", async () => {
    mockSession = ANONYMOUS;
    mockTap = tap("BOOKING_APPROVED");

    await render(page());

    expect(router.navigate).toHaveBeenCalledWith("/bookings");
  });

  it("also opens it when the app is already running", async () => {
    const screen = await render(page());
    expect(router.navigate).not.toHaveBeenCalled();

    mockTap = tap("BOOKING_CONFIRMED");
    await screen.rerender(page());

    expect(router.navigate).toHaveBeenCalledWith("/bookings");
  });

  it.each([
    ["no answer yet", undefined],
    ["no tap", null],
    ["a push about something else", tap("OTP_SIGNIN")],
    ["a push with no code", tap(undefined)],
    ["a code that is not text", tap(42)],
    ["an action that is not the plain tap", tap("BOOKING_CONFIRMED", "other-action")],
  ])("goes nowhere on %s", async (_, response) => {
    mockTap = response;

    await render(page());

    expect(router.navigate).not.toHaveBeenCalled();
  });

  it("is cleared even when it goes nowhere", async () => {
    mockTap = tap("OTP_SIGNIN");

    await render(page());

    expect(Notifications.clearLastNotificationResponseAsync).toHaveBeenCalledTimes(1);
  });
});

describe("in Expo Go", () => {
  it("never reaches for a tap", async () => {
    mockPhone.device.supported = false;
    mockTap = tap("BOOKING_REMINDER");

    await render(page());

    expect(router.navigate).not.toHaveBeenCalled();
  });
});

it("refuses to be used without its provider", async () => {
  const error = jest.spyOn(console, "error").mockImplementation(() => {});
  function Lonely() {
    usePush();
    return null;
  }

  await expect(render(<Lonely />)).rejects.toThrow("PushProvider");
  error.mockRestore();
});
