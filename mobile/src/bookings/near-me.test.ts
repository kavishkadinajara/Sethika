import appConfig from "../../app.json";

import { createTranslator } from "@/i18n/translate";

import { DEFAULT_RADIUS_KM, formatKm, inSriLanka, locate, LOCATE_WAIT_MS, nearDate, RADII_KM, roundCoordinate, type LocationDevice } from "./near-me";

/*  The order of asking and what leaves the phone, against a scripted phone. */

declare const __dirname: string;

const { readFileSync } = jest.requireActual<{ readFileSync: (path: string, encoding: "utf8") => string }>("fs");
const backend = (path: string) => readFileSync(`${__dirname}/../../../src/${path}`, "utf8");
const COLOMBO = { latitude: 6.92714, longitude: 79.86155 };

function phone(overrides: Partial<LocationDevice> = {}) {
  return {
    requestPermission: jest.fn(() => Promise.resolve(true)),
    lastKnown: jest.fn(() => Promise.resolve(null)),
    current: jest.fn(() => Promise.resolve(COLOMBO)),
    ...overrides,
  } as jest.Mocked<LocationDevice>;
}

describe("locate", () => {
  it("rounds the point to about 100 m before anything else sees it", async () => {
    expect(await locate(phone())).toEqual({ ok: true, point: { latitude: 6.927, longitude: 79.862 } });
  });

  it("asks for permission first, and touches nothing else on a refusal", async () => {
    const device = phone({ requestPermission: jest.fn(() => Promise.resolve(false)) });

    expect(await locate(device)).toEqual({ ok: false, problem: "denied" });
    expect(device.lastKnown).not.toHaveBeenCalled();
    expect(device.current).not.toHaveBeenCalled();
  });

  it("uses a recent position when the phone has one, without waiting for a new fix", async () => {
    const device = phone({ lastKnown: jest.fn(() => Promise.resolve({ latitude: 7.2906, longitude: 80.6337 })) });

    expect(await locate(device)).toEqual({ ok: true, point: { latitude: 7.291, longitude: 80.634 } });
    expect(device.current).not.toHaveBeenCalled();
  });

  it("gets a new fix when the recent position cannot be read", async () => {
    const device = phone({ lastKnown: jest.fn(() => Promise.reject(new Error("no provider"))) });

    expect(await locate(device)).toMatchObject({ ok: true });
    expect(device.current).toHaveBeenCalledTimes(1);
  });

  it("says outside for a point the API would refuse", async () => {
    const device = phone({ current: jest.fn(() => Promise.resolve({ latitude: 51.5, longitude: -0.12 })) });

    expect(await locate(device)).toEqual({ ok: false, problem: "outside" });
  });

  it("judges the rounded point, the one the API will see", async () => {
    const device = phone({ current: jest.fn(() => Promise.resolve({ latitude: 5.4996, longitude: 80 })) });

    expect(await locate(device)).toEqual({ ok: true, point: { latitude: 5.5, longitude: 80 } });
  });

  it("turns a phone that fails, at either step, into unavailable", async () => {
    expect(await locate(phone({ current: jest.fn(() => Promise.reject(new Error("location off"))) }))).toEqual({ ok: false, problem: "unavailable" });
    expect(await locate(phone({ requestPermission: jest.fn(() => Promise.reject(new Error("no plist"))) }))).toEqual({ ok: false, problem: "unavailable" });
  });

  describe("a phone that never answers", () => {
    beforeEach(() => {
      jest.useFakeTimers();
    });
    afterEach(() => {
      jest.useRealTimers();
    });

    const never = () => new Promise<never>(() => {});

    it("gives up after the wait, not before", async () => {
      const outcome = jest.fn();
      void locate(phone({ current: jest.fn(never) })).then(outcome);

      await jest.advanceTimersByTimeAsync(LOCATE_WAIT_MS - 1);
      expect(outcome).not.toHaveBeenCalled();

      await jest.advanceTimersByTimeAsync(1);
      expect(outcome).toHaveBeenCalledWith({ ok: false, problem: "unavailable" });
    });

    it("counts a stuck recent position against the same wait", async () => {
      const outcome = jest.fn();
      void locate(phone({ lastKnown: jest.fn(never) })).then(outcome);

      await jest.advanceTimersByTimeAsync(LOCATE_WAIT_MS);
      expect(outcome).toHaveBeenCalledWith({ ok: false, problem: "unavailable" });
    });

    it("waits as the web does, and leaves no timer behind", async () => {
      expect(LOCATE_WAIT_MS).toBe(10_000);

      await locate(phone());
      expect(jest.getTimerCount()).toBe(0);
    });

    it("does not start the wait until the donor has answered the phone's question", async () => {
      let answer: (granted: boolean) => void = () => {};
      const outcome = jest.fn();
      void locate(phone({ requestPermission: jest.fn(() => new Promise<boolean>((resolve) => (answer = resolve))) })).then(outcome);

      await jest.advanceTimersByTimeAsync(LOCATE_WAIT_MS * 3);
      expect(outcome).not.toHaveBeenCalled();

      answer(true);
      await jest.advanceTimersByTimeAsync(0);
      expect(outcome).toHaveBeenCalledWith(expect.objectContaining({ ok: true }));
    });
  });
});

describe("nearDate", () => {
  it("counts days on Colombo's calendar, not the phone's", () => {
    // 20:00 UTC is already 01:30 the next day in Colombo.
    const now = new Date("2026-11-30T20:00:00Z");

    expect(nearDate("today", now)).toBe("2026-12-01");
    expect(nearDate("tomorrow", now)).toBe("2026-12-02");
  });
});

describe("the small rules", () => {
  it("rounds to three decimals", () => {
    expect(roundCoordinate(6.92714)).toBe(6.927);
    expect(roundCoordinate(79.86155)).toBe(79.862);
  });

  it.each([
    [5.5, 80, true],
    [10, 80, true],
    [7, 79.3, true],
    [7, 82.1, true],
    [5.49, 80, false],
    [10.01, 80, false],
    [7, 79.29, false],
    [7, 82.11, false],
  ])("holds %d, %d to Sri Lanka's box: %s", (latitude, longitude, inside) => {
    expect(inSriLanka({ latitude, longitude })).toBe(inside);
  });

  it("shows a distance to one decimal, without a trailing zero", () => {
    expect(formatKm(2.35)).toBe("2.4");
    expect(formatKm(12.04)).toBe("12");
    expect(formatKm(0.26)).toBe("0.3");
  });

  it("puts the number where each language wants it", () => {
    expect(createTranslator("en")("App.near.distance", { km: "2.4" })).toBe("2.4 km away");
    expect(createTranslator("si")("App.near.distance", { km: "2.4" })).toBe("කි.මී. 2.4ක් දුරින්");
    expect(createTranslator("ta")("App.near.distance", { km: "2.4" })).toBe("2.4 கி.மீ. தொலைவில்");
  });
});

describe("what must stay equal to the API and the app's config", () => {
  it("offers the API's radii and its default", () => {
    const search = backend("Sethika.Application/Bookings/Discovery/AvailabilitySearch.cs");

    expect(search).toContain(`RadiiKm { get; } = [${RADII_KM.join(", ")}];`);
    expect(search).toContain(`DefaultRadiusKm = ${DEFAULT_RADIUS_KM};`);
  });

  it("holds a point to the API's box", () => {
    expect(backend("Sethika.Domain/Institutions/GeoPoint.cs")).toContain(
      "Latitude is >= 5.5 and <= 10.0 && Longitude is >= 79.3 and <= 82.1",
    );
  });

  it("stays foreground only in the build (F33)", () => {
    const entry = appConfig.expo.plugins.find((plugin) => Array.isArray(plugin) && plugin[0] === "expo-location") as [string, Record<string, unknown>];

    expect(entry[1]).toMatchObject({
      isAndroidBackgroundLocationEnabled: false,
      isIosBackgroundLocationEnabled: false,
      locationAlwaysPermission: false,
      locationAlwaysAndWhenInUsePermission: false,
    });
    expect(entry[1].locationWhenInUsePermission).toEqual(expect.stringContaining("Near me"));
  });
});
