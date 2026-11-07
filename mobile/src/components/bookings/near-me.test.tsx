import { router } from "expo-router";
import { Linking } from "react-native";

import { addDays, colomboToday } from "@/bookings/calendar";
import type { LocationDevice } from "@/bookings/near-me";
import { createApiStub } from "@/test-utils/api-stub";
import { LUNCH, DINNER, searchAnswer, searchPlace, searchSlot } from "@/test-utils/fixtures";
import { json, press, render, settle } from "@/test-utils/render";
import { Text } from "@/components/text";

import { NearMe } from "./near-me";

/*  Near me on Explore, against a scripted phone and GET /api/search (anonymous), stubbed at the
    app's boundary. Its contract: SearchEndpoints.cs, SearchResultView; `distanceKm` to 10 m.    */

const mockApi = createApiStub();

jest.mock("@/api/http", () => ({ getJson: (path: string) => mockApi.getJson(path) }));
jest.mock("@/auth/session-provider", () => ({ useSession: () => ({ status: "anonymous" }), useSessionClient: () => mockApi.client }));
jest.mock("expo-router", () => ({ router: { push: jest.fn(), navigate: jest.fn(), dismissTo: jest.fn() } }));

const today = colomboToday();
const tomorrow = addDays(today, 1);
const path = (date: string, radiusKm = 10) => `/api/search?date=${date}&near=6.927,79.862&radiusKm=${radiusKm}`;
const GET = (date: string, radiusKm?: number) => `GET ${path(date, radiusKm)}`;
const searches = () => mockApi.sent.filter((request) => request.path.startsWith("/api/search"));

const DIRECTORY = "The directory";

function phone(overrides: Partial<LocationDevice> = {}) {
  return {
    requestPermission: jest.fn(() => Promise.resolve(true)),
    lastKnown: jest.fn(() => Promise.resolve(null)),
    current: jest.fn(() => Promise.resolve({ latitude: 6.92714, longitude: 79.86155 })),
    ...overrides,
  } as jest.Mocked<LocationDevice>;
}

const nearMe = (device: LocationDevice, props: { refreshKey?: number; onSettled?: () => void } = {}) => (
  <NearMe device={device} {...props}>
    <Text>{DIRECTORY}</Text>
  </NearMe>
);

async function located(device: LocationDevice = phone(), props: { refreshKey?: number; onSettled?: () => void } = {}) {
  const screen = await render(nearMe(device, props));
  await press(screen.getText("Near me"));
  return screen;
}

beforeEach(() => {
  mockApi.sent.length = 0;
  mockApi.serve({ [GET(tomorrow)]: () => json(200, searchAnswer(tomorrow, [searchPlace()])) });
  jest.mocked(router.push).mockClear();
});

it("asks the phone for nothing and searches nothing until the donor taps", async () => {
  const device = phone();
  const screen = await render(nearMe(device));

  screen.getText(DIRECTORY);
  expect(device.requestPermission).not.toHaveBeenCalled();
  expect(device.lastKnown).not.toHaveBeenCalled();
  expect(device.current).not.toHaveBeenCalled();
  expect(mockApi.sent).toEqual([]);
});

it("sends the rounded point anonymously, for tomorrow within 10 km, and lists what comes back", async () => {
  mockApi.serve({
    [GET(tomorrow)]: () =>
      json(200, searchAnswer(tomorrow, [searchPlace({ slug: "near", name: "Near Home", distanceKm: 1.26 }), searchPlace({ slug: "far", name: "Far Home", distanceKm: 8 })])),
  });
  const screen = await located();

  expect(mockApi.sent).toEqual([{ method: "GET", path: path(tomorrow), body: null, signedIn: false }]);
  expect(screen.queryText(DIRECTORY)).toBeNull();
  expect(screen.allText().filter((line) => / Home$/.test(line))).toEqual(["Near Home", "Far Home"]);
  screen.getText("1.3 km away");
  screen.getText("8 km away");
  screen.getText("2 places found.");
  screen.getLabel(/^Near Home\. Elders' home\. Peradeniya, Kandy\. 1\.3 km away$/);
});

it("says the list is places open that day, not every place", async () => {
  const screen = await located();

  screen.getText("Every place shown has a meal you can sponsor that day.");
  screen.getText(/^Open on .+ 20\d\d$/);
});

it("shows the meals open that day and what is left of them", async () => {
  mockApi.serve({
    [GET(tomorrow)]: () =>
      json(200, searchAnswer(tomorrow, [searchPlace({ slots: [searchSlot({ sponsors: 1 }), searchSlot({ mealSlot: DINNER, name: "Dinner", startTime: "18:00", endTime: "19:30", sponsors: 3 })] })])),
  });
  const screen = await located();

  screen.getText("Lunch · 11:30–13:00");
  screen.getText("1 sponsor place left");
  screen.getText("Dinner · 18:00–19:30");
  screen.getText("3 sponsor places left");
});

it("opens a place's page from its card", async () => {
  const screen = await located();

  await press(screen.getLabel(/^Test Elders' Home\. /));
  expect(router.push).toHaveBeenCalledWith({ pathname: "/i/[slug]", params: { slug: "test-elders-home" } });
});

it("opens the booking sheet on the meal and the answer's day", async () => {
  const screen = await located();

  await press(screen.getLabel("Book Lunch at Test Elders' Home"));
  expect(router.push).toHaveBeenCalledWith({ pathname: "/i/[slug]", params: { slug: "test-elders-home", slot: LUNCH, date: tomorrow } });
});

it("searches the other day and a wider circle from the same point, without asking the phone again", async () => {
  mockApi.also({
    [GET(today)]: () => json(200, searchAnswer(today, [searchPlace({ name: "Open Today" })])),
    [GET(today, 25)]: () => json(200, searchAnswer(today, [searchPlace({ name: "Further Out" })])),
  });
  const device = phone();
  const screen = await located(device);

  await press(screen.getText("Today"));
  screen.getText("Open Today");
  await press(screen.getText("25 km"));
  screen.getText("Further Out");

  expect(searches().map((request) => request.path)).toEqual([path(tomorrow), path(today), path(today, 25)]);
  expect(device.current).toHaveBeenCalledTimes(1);
});

it("marks the chosen day and radius", async () => {
  const screen = await located();
  const radios = screen.where((props) => props.accessibilityRole === "radio").map((chip) => chip.props.accessibilityState.selected);
  expect(radios).toEqual([false, true, false, true, false, false]);
});

it("offers exactly the two days and the API's four radii", async () => {
  const screen = await located();

  for (const label of ["Today", "Tomorrow", "5 km", "10 km", "25 km", "50 km"]) screen.getText(label);
  screen.getLabel("Day");
  screen.getLabel("Within");
});

it("does not leave the last answer showing under a new choice", async () => {
  let answer: (response: Response) => void = () => {};
  mockApi.also({ [GET(today)]: () => new Promise<Response>((resolve) => (answer = resolve)) });
  const screen = await located();

  await press(screen.getText("Today"));
  expect(screen.queryText("Test Elders' Home")).toBeNull();
  screen.getLabel("Loading");

  answer(json(200, searchAnswer(today, [searchPlace({ name: "Open Today" })])));
  await settle();
  screen.getText("Open Today");
});

it("keeps the newest choice when an older answer arrives after it", async () => {
  let older: (response: Response) => void = () => {};
  mockApi.serve({
    [GET(tomorrow)]: () => json(200, searchAnswer(tomorrow, [searchPlace()])),
    [GET(today)]: () => new Promise<Response>((resolve) => (older = resolve)),
    [GET(today, 50)]: () => json(200, searchAnswer(today, [searchPlace({ name: "The Newest" })])),
  });
  const screen = await located();

  await press(screen.getText("Today"));
  await press(screen.getText("50 km"));
  screen.getText("The Newest");

  older(json(200, searchAnswer(today, [searchPlace({ name: "The Older" })])));
  await settle();
  screen.getText("The Newest");
  expect(screen.queryText("The Older")).toBeNull();
});

it.each([
  ["Location is turned off for Sethika. Allow it in your phone's settings, or choose from the list below.", { requestPermission: jest.fn(() => Promise.resolve(false)) }],
  ["You seem to be outside Sri Lanka. Choose from the list below instead.", { current: jest.fn(() => Promise.resolve({ latitude: 51.5, longitude: -0.12 })) }],
  ["Your location could not be found. Try again, or choose from the list below.", { current: jest.fn(() => Promise.reject(new Error("off"))) }],
])("says why and keeps the directory: %s", async (message, overrides) => {
  const screen = await located(phone(overrides));

  screen.getText(message);
  screen.getText(DIRECTORY);
  screen.getText("Near me");
  expect(mockApi.sent).toEqual([]);
});

it("offers the phone's settings after a refusal, and only then", async () => {
  const open = jest.spyOn(Linking, "openSettings").mockResolvedValue();
  const refused = await located(phone({ requestPermission: jest.fn(() => Promise.resolve(false)) }));

  await press(refused.getText("Open settings"));
  expect(open).toHaveBeenCalledTimes(1);

  const lost = await located(phone({ current: jest.fn(() => Promise.reject(new Error("off"))) }));
  expect(lost.queryText("Open settings")).toBeNull();
});

it("clears the problem when the donor tries again, and works this time", async () => {
  const device = phone({ requestPermission: jest.fn<Promise<boolean>, []>().mockResolvedValueOnce(false).mockResolvedValueOnce(true) });
  const screen = await located(device);
  screen.getText(/^Location is turned off/);

  await press(screen.getText("Near me"));

  expect(screen.queryText(/^Location is turned off/)).toBeNull();
  screen.getText("Test Elders' Home");
});

it("clears an old problem as soon as the next try starts", async () => {
  let granted: (value: boolean) => void = () => {};
  const device = phone({
    requestPermission: jest.fn<Promise<boolean>, []>().mockResolvedValueOnce(false).mockImplementationOnce(() => new Promise((resolve) => (granted = resolve))),
  });
  const screen = await located(device);

  await press(screen.getText("Near me"));
  expect(screen.queryText(/^Location is turned off/)).toBeNull();
  granted(false);
  await settle();
  screen.getText(/^Location is turned off/);
});

it("is busy while the phone looks, so a second tap asks nothing more", async () => {
  let granted: (value: boolean) => void = () => {};
  const device = phone({ requestPermission: jest.fn(() => new Promise<boolean>((resolve) => (granted = resolve))) });
  const screen = await located(device);

  const button = screen.getText("Finding you…");
  await press(button);
  expect(device.requestPermission).toHaveBeenCalledTimes(1);

  granted(true);
  await settle();
  screen.getText("Test Elders' Home");
});

it("says so when nothing is open here, on that day", async () => {
  mockApi.serve({ [GET(tomorrow)]: () => json(200, searchAnswer(tomorrow, [])) });
  const screen = await located();

  screen.getText(/^No place has a meal you can sponsor on .+ here\. Try another day or a wider area\.$/);
  expect(screen.queryText(/places? found/)).toBeNull();
});

it("says these are the nearest 100 when more matched, and not otherwise", async () => {
  mockApi.serve({ [GET(tomorrow)]: () => json(200, { ...searchAnswer(tomorrow, [searchPlace()]), more: true }) });
  const more = await located();
  more.getText("1 place found. These are the nearest 100.");

  mockApi.serve({ [GET(tomorrow)]: () => json(200, searchAnswer(tomorrow, [searchPlace()])) });
  const all = await located();
  all.getText("1 place found.");
});

it("leaves the distance out if the API gives none", async () => {
  mockApi.serve({ [GET(tomorrow)]: () => json(200, searchAnswer(tomorrow, [searchPlace({ distanceKm: null })])) });
  const screen = await located();

  expect(screen.queryText(/km away/)).toBeNull();
  screen.getLabel(/^Test Elders' Home\. Elders' home\. Peradeniya, Kandy$/);
});

it("offers a retry when the search fails, and keeps the donor's choices", async () => {
  let fail = true;
  mockApi.serve({ [GET(tomorrow, 10)]: () => (fail ? json(500, {}) : json(200, searchAnswer(tomorrow, [searchPlace()]))) });
  const screen = await located();
  screen.getText("The places could not be loaded.");
  screen.getText("Tomorrow");

  fail = false;
  await press(screen.getText("Try again"));

  expect(screen.queryText("The places could not be loaded.")).toBeNull();
  screen.getText("Test Elders' Home");
  expect(searches()).toHaveLength(2);
});

it("shows the loading state during a retry", async () => {
  let answer: (response: Response) => void = () => {};
  const answers: (() => Response | Promise<Response>)[] = [() => json(500, {}), () => new Promise<Response>((resolve) => (answer = resolve))];
  mockApi.serve({ [GET(tomorrow)]: () => answers.shift()!() });
  const screen = await located();

  await press(screen.getText("Try again"));
  expect(screen.queryText("The places could not be loaded.")).toBeNull();
  screen.getLabel("Loading");
  answer(json(200, searchAnswer(tomorrow, [searchPlace()])));
  await settle();
});

it("clears a failure when another choice is made", async () => {
  let answer: (response: Response) => void = () => {};
  mockApi.serve({
    [GET(tomorrow)]: () => json(500, {}),
    [GET(today)]: () => new Promise<Response>((resolve) => (answer = resolve)),
  });
  const screen = await located();
  screen.getText("The places could not be loaded.");

  await press(screen.getText("Today"));
  expect(screen.queryText("The places could not be loaded.")).toBeNull();
  answer(json(200, searchAnswer(today, [searchPlace()])));
  await settle();
  screen.getText("Test Elders' Home");
});

it("searches again from the same point on a pull, keeps the list meanwhile, and stops the spinner", async () => {
  const device = phone();
  const onSettled = jest.fn();
  const screen = await located(device, { refreshKey: 0, onSettled });
  expect(onSettled).toHaveBeenCalledTimes(1);

  let answer: (response: Response) => void = () => {};
  mockApi.serve({ [GET(tomorrow)]: () => new Promise<Response>((resolve) => (answer = resolve)) });
  await screen.rerender(nearMe(device, { refreshKey: 1, onSettled }));
  screen.getText("Test Elders' Home");

  answer(json(200, searchAnswer(tomorrow, [searchPlace({ name: "Newly Open" })])));
  await settle();
  screen.getText("Newly Open");
  expect(onSettled).toHaveBeenCalledTimes(2);
  expect(device.current).toHaveBeenCalledTimes(1);
});

it("recovers from a failure on a pull", async () => {
  const device = phone();
  mockApi.serve({ [GET(tomorrow)]: () => json(500, {}) });
  const onSettled = jest.fn();
  const screen = await located(device, { refreshKey: 0, onSettled });
  expect(onSettled).toHaveBeenCalledTimes(1);

  mockApi.serve({ [GET(tomorrow)]: () => json(200, searchAnswer(tomorrow, [searchPlace()])) });
  await screen.rerender(nearMe(device, { refreshKey: 1, onSettled }));

  expect(screen.queryText("The places could not be loaded.")).toBeNull();
  screen.getText("Test Elders' Home");
});

it("ignores a pull in the directory: the directory has its own", async () => {
  const device = phone();
  const screen = await render(nearMe(device, { refreshKey: 0 }));

  await screen.rerender(nearMe(device, { refreshKey: 1 }));
  expect(mockApi.sent).toEqual([]);
});

it("forgets the point when the donor goes back to every place", async () => {
  const device = phone();
  const screen = await located(device);

  await press(screen.getText("Show every place"));
  screen.getText(DIRECTORY);
  expect(screen.queryText("Test Elders' Home")).toBeNull();

  await press(screen.getText("Near me"));
  expect(device.current).toHaveBeenCalledTimes(2);
  screen.getText("Test Elders' Home");
});

it("drops an answer that arrives after the donor went back", async () => {
  let answer: (response: Response) => void = () => {};
  mockApi.serve({ [GET(tomorrow)]: () => new Promise<Response>((resolve) => (answer = resolve)) });
  const screen = await located();

  await press(screen.getText("Show every place"));
  answer(json(200, searchAnswer(tomorrow, [searchPlace()])));
  await settle();

  screen.getText(DIRECTORY);
  mockApi.serve({ [GET(tomorrow)]: () => new Promise<Response>(() => {}) });
  await press(screen.getText("Near me"));
  expect(screen.queryText("Test Elders' Home")).toBeNull();
});

it("starts the next visit clean after a failure", async () => {
  mockApi.serve({ [GET(tomorrow)]: () => json(500, {}) });
  const screen = await located();

  await press(screen.getText("Show every place"));
  mockApi.serve({ [GET(tomorrow)]: () => new Promise<Response>(() => {}) });
  await press(screen.getText("Near me"));

  expect(screen.queryText("The places could not be loaded.")).toBeNull();
  screen.getLabel("Loading");
});

it("starts the next visit without the last list", async () => {
  const screen = await located();

  await press(screen.getText("Show every place"));
  mockApi.serve({ [GET(tomorrow)]: () => new Promise<Response>(() => {}) });
  await press(screen.getText("Near me"));

  expect(screen.queryText("Test Elders' Home")).toBeNull();
  screen.getLabel("Loading");
});
