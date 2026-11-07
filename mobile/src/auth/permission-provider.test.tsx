import { act } from "react";
import { AppState, Text } from "react-native";

import { ANONYMOUS, createApiStub, DONOR, STAFF } from "@/test-utils/api-stub";
import { json, render, settle } from "@/test-utils/render";

import type { SessionState } from "./session-client";
import { PermissionProvider, usePermissions } from "./permission-provider";

/*  The provider against GET /api/me/permissions, stubbed at the app's client boundary. A session
    is swapped by rendering again, as the session client's subscription would.                 */

const mockApi = createApiStub();
let mockSession: SessionState = STAFF;

jest.mock("./session-provider", () => ({ useSession: () => mockSession, useSessionClient: () => mockApi.client }));

const VIEW = "BOOKINGS.CALENDAR.VIEW";
const page = () => (
  <PermissionProvider>
    <Probe />
  </PermissionProvider>
);

function Probe() {
  const { ready, can } = usePermissions();
  return <Text>{`${ready ? "ready" : "waiting"} ${can(VIEW) ? "can" : "cannot"}`}</Text>;
}

const permissions = (...codes: string[]) => () => json(200, { permissions: codes });
const reads = () => mockApi.sent.filter((request) => request.path === "/api/me/permissions").length;

/** Tells the provider the phone came back to the app. */
async function comeBack() {
  const emit = (AppState.addEventListener as jest.Mock).mock.calls.at(-1)?.[1] as (state: string) => void;
  await act(async () => emit("active"));
  await settle();
}

beforeEach(() => {
  mockApi.sent.length = 0;
  mockSession = STAFF;
  mockApi.serve({ "GET /api/me/permissions": permissions(VIEW) });
  jest.spyOn(AppState, "addEventListener").mockImplementation(() => ({ remove: () => {} }) as never);
});

afterEach(() => {
  jest.restoreAllMocks();
});

it("reads the session's permissions, signed in", async () => {
  const screen = await render(page());

  screen.getText("ready can");
  expect(mockApi.sent).toEqual([{ method: "GET", path: "/api/me/permissions", body: null, signedIn: true }]);
});

it("holds nothing it was not given", async () => {
  mockApi.serve({ "GET /api/me/permissions": permissions("BOOKINGS.SLOTS.VIEW") });

  const screen = await render(page());

  screen.getText("ready cannot");
});

it("asks nothing of a visitor, and stays closed", async () => {
  mockSession = ANONYMOUS;

  const screen = await render(page());

  screen.getText("waiting cannot");
  expect(reads()).toBe(0);
});

it("keeps every gate closed while the answer is on its way", async () => {
  let answer: (response: Response) => void = () => {};
  mockApi.serve({ "GET /api/me/permissions": () => new Promise((resolve) => (answer = resolve)) });

  const screen = await render(page());
  screen.getText("waiting cannot");

  await act(async () => answer(json(200, { permissions: [VIEW] })));
  await settle();
  screen.getText("ready can");
});

it("does not read again for a new access token on the same session", async () => {
  const screen = await render(page());

  mockSession = { ...STAFF, accessToken: "access-2" } as SessionState;
  await screen.rerender(page());

  expect(reads()).toBe(1);
});

describe("moving to another institution", () => {
  const OTHER: SessionState = { ...STAFF, activeInstitution: "beta-temple" } as SessionState;

  it("closes every gate at once, and opens on the new answer alone", async () => {
    const screen = await render(page());
    screen.getText("ready can");

    let answer: (response: Response) => void = () => {};
    mockApi.serve({ "GET /api/me/permissions": () => new Promise((resolve) => (answer = resolve)) });
    mockSession = OTHER;
    await screen.rerender(page());
    screen.getText("waiting cannot");

    await act(async () => answer(json(200, { permissions: [] })));
    await settle();
    screen.getText("ready cannot");
  });

  it("drops an answer that arrives after the session moved on", async () => {
    let first: (response: Response) => void = () => {};
    mockApi.serve({ "GET /api/me/permissions": () => new Promise((resolve) => (first = resolve)) });
    const screen = await render(page());

    mockApi.serve({ "GET /api/me/permissions": permissions() });
    mockSession = OTHER;
    await screen.rerender(page());
    await act(async () => first(json(200, { permissions: [VIEW] })));
    await settle();

    screen.getText("ready cannot");
  });

  it("reads again going back to the person's own account", async () => {
    const screen = await render(page());
    mockApi.serve({ "GET /api/me/permissions": permissions() });

    mockSession = DONOR;
    await screen.rerender(page());

    expect(reads()).toBe(2);
    screen.getText("ready cannot");
  });
});

it("forgets on sign-out, so the next account never opens on the last one's answer", async () => {
  const screen = await render(page());

  mockSession = ANONYMOUS;
  await screen.rerender(page());
  screen.getText("waiting cannot");

  let answer: (response: Response) => void = () => {};
  mockApi.serve({ "GET /api/me/permissions": () => new Promise((resolve) => (answer = resolve)) });
  mockSession = STAFF;
  await screen.rerender(page());

  screen.getText("waiting cannot");
  await act(async () => answer(json(200, { permissions: [] })));
  await settle();
  screen.getText("ready cannot");
});

describe("coming back to the app", () => {
  it("reads again, so a role changed meanwhile shows", async () => {
    const screen = await render(page());
    mockApi.serve({ "GET /api/me/permissions": permissions() });

    await comeBack();

    screen.getText("ready cannot");
  });

  it("does nothing for any other change of state", async () => {
    await render(page());
    const emit = (AppState.addEventListener as jest.Mock).mock.calls.at(-1)?.[1] as (state: string) => void;

    await act(async () => emit("background"));
    await settle();

    expect(reads()).toBe(1);
  });

  it.each<[string, () => Response | never]>([
    ["a lost connection", () => { throw new Error("offline"); }],
    ["a server error", () => new Response(null, { status: 500 })],
    ["a refusal", () => json(403, { code: "forbidden" })],
    ["an answer that is not the API's", () => json(200, { permissions: "all" })],
  ])("keeps what the session had after %s", async (_, answer) => {
    const screen = await render(page());
    mockApi.serve({ "GET /api/me/permissions": answer });

    await comeBack();

    screen.getText("ready can");
  });

  it("recovers from a first read that failed", async () => {
    mockApi.serve({ "GET /api/me/permissions": () => { throw new Error("offline"); } });
    const screen = await render(page());
    screen.getText("ready cannot");

    mockApi.serve({ "GET /api/me/permissions": permissions(VIEW) });
    await comeBack();

    screen.getText("ready can");
  });
});

it("stops listening when it goes", async () => {
  const remove = jest.fn();
  jest.mocked(AppState.addEventListener).mockImplementation(() => ({ remove }) as never);

  const screen = await render(page());
  screen.unmount();

  expect(remove).toHaveBeenCalled();
});
