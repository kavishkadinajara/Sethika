import { router } from "expo-router";
import { act } from "react";
import { Linking } from "react-native";

import { MAP_BASE_URL, OSM_COPYRIGHT } from "@/bookings/map-page";
import { searchPlace } from "@/test-utils/fixtures";
import { press, render, type Screen } from "@/test-utils/render";

import { NearMap } from "./near-map";

/*  The map's screen around a stand-in WebView: what it hands the WebView, and what it does with
    what comes back. The page's own text is map-page.test.ts.                                    */

jest.mock("react-native-webview", () => {
  const { createElement } = jest.requireActual("react");
  const { View } = jest.requireActual("react-native");
  return { WebView: (props: object) => createElement(View, props) };
});
jest.mock("expo-constants", () => ({ __esModule: true, default: { expoConfig: { version: "1.2.3" } } }));
jest.mock("expo-router", () => ({ router: { push: jest.fn() } }));

const PLACES = [searchPlace({ slug: "near", name: "Near Home" }), searchPlace({ slug: "far", name: "Far Home" })];
const POINT = { latitude: 6.927, longitude: 79.862 };
const FAILED = "The map did not load fully. The list shows the same places.";
const HINT = "The list shows the same places.";

async function open() {
  const onClose = jest.fn();
  const screen = await render(<NearMap places={PLACES} point={POINT} radiusKm={25} onClose={onClose} />);
  return { screen, onClose };
}

const web = (screen: Screen) => screen.getTestId("near-map").props;
const modal = (screen: Screen) => screen.where((props) => typeof props.onRequestClose === "function")[0];

async function says(screen: Screen, message: object | string) {
  const data = typeof message === "string" ? message : JSON.stringify(message);
  await act(async () => {
    web(screen).onMessage({ nativeEvent: { data } });
  });
}

beforeEach(() => {
  jest.mocked(router.push).mockClear();
});

it("hands the WebView the page at its address, named as the app, in one window", async () => {
  const { screen } = await open();
  const props = web(screen);

  expect(props.source.baseUrl).toBe(MAP_BASE_URL);
  expect(props.source.html).toContain('"slug":"near"');
  expect(props.source.html).toContain('"slug":"far"');
  expect(props.source.html).toContain('"latitude":6.927,"longitude":79.862,"radiusKm":25');
  expect(props.userAgent).toBe("Sethika/1.2.3 (+https://sethika.com)");
  expect(props.setSupportMultipleWindows).toBe(false);
});

it("leaves the WebView's cache on, as OSM asks", async () => {
  const { screen } = await open();
  const props = web(screen);

  expect(props.incognito).toBeUndefined();
  expect(props.cacheEnabled).toBeUndefined();
});

it("names the map and says where else the same places are", async () => {
  const { screen } = await open();

  screen.getText("Map of the places found");
  screen.getText(HINT);
  expect(web(screen).accessibilityLabel).toBe("Map of the places found");
  expect(screen.queryText(FAILED)).toBeNull();
});

it("shows it is loading until the page says it is ready", async () => {
  const { screen } = await open();
  screen.getLabel("Loading");

  await says(screen, { type: "tiles" });
  screen.getLabel("Loading");

  await says(screen, { type: "ready" });
  expect(screen.queryLabel("Loading")).toBeNull();
});

it("closes from its button and from the phone's back", async () => {
  const { screen, onClose } = await open();

  await press(screen.getText("Close"));
  expect(onClose).toHaveBeenCalledTimes(1);

  modal(screen).props.onRequestClose();
  expect(onClose).toHaveBeenCalledTimes(2);
});

it("covers the whole screen, so that a sheet's swipe cannot fight the map's", async () => {
  const { screen } = await open();

  expect(modal(screen).props.presentationStyle ?? "fullScreen").toBe("fullScreen");
});

it("opens the place's page from a pin and leaves the map", async () => {
  const { screen, onClose } = await open();

  await says(screen, { type: "open", slug: "far" });

  expect(onClose).toHaveBeenCalledTimes(1);
  expect(router.push).toHaveBeenCalledWith({ pathname: "/i/[slug]", params: { slug: "far" } });
});

it("opens nothing for a place it did not put on the map", async () => {
  const { screen, onClose } = await open();

  await says(screen, { type: "open", slug: "elsewhere" });
  await says(screen, "not json");

  expect(onClose).not.toHaveBeenCalled();
  expect(router.push).not.toHaveBeenCalled();
});

it("says so when tiles fail, and keeps the map with its pins", async () => {
  const { screen } = await open();

  await says(screen, { type: "tiles" });

  screen.getText(FAILED);
  expect(screen.queryText(HINT)).toBeNull();
  expect(screen.queryTestId("near-map")).not.toBeNull();
});

it.each(["onError", "onRenderProcessGone", "onContentProcessDidTerminate"])("takes a dead WebView away after %s, and leaves the way back", async (event) => {
  const { screen, onClose } = await open();

  await act(async () => {
    web(screen)[event]({ nativeEvent: {} });
  });

  expect(screen.queryTestId("near-map")).toBeNull();
  expect(screen.queryLabel("Loading")).toBeNull();
  screen.getText(FAILED);
  await press(screen.getText("Close"));
  expect(onClose).toHaveBeenCalled();
});

describe("links in the page", () => {
  const openURL = jest.spyOn(Linking, "openURL");

  beforeEach(() => {
    openURL.mockReset();
    openURL.mockResolvedValue(undefined);
  });

  it("lets the page itself load", async () => {
    const { screen } = await open();

    expect(web(screen).onShouldStartLoadWithRequest({ url: MAP_BASE_URL })).toBe(true);
    expect(openURL).not.toHaveBeenCalled();
  });

  it("opens OSM's licence in the phone's browser, not in the map", async () => {
    const { screen } = await open();

    expect(web(screen).onShouldStartLoadWithRequest({ url: OSM_COPYRIGHT })).toBe(false);
    expect(openURL).toHaveBeenCalledWith(OSM_COPYRIGHT);
  });

  it("stops anything else, and does not open it", async () => {
    const { screen } = await open();

    expect(web(screen).onShouldStartLoadWithRequest({ url: "https://evil.example/" })).toBe(false);
    expect(openURL).not.toHaveBeenCalled();
  });
});
