import * as Location from "expo-location";

import { nativeLocation } from "./native-location";

/*  The real phone's calls, read from expo-location 58's own contract: only the foreground
    permission, a position that may be five minutes old, and a fix at the rounding's accuracy. */

jest.mock("expo-location", () => ({
  Accuracy: { Balanced: 3 },
  requestForegroundPermissionsAsync: jest.fn(),
  requestBackgroundPermissionsAsync: jest.fn(),
  getLastKnownPositionAsync: jest.fn(),
  getCurrentPositionAsync: jest.fn(),
}));

const location = jest.mocked(Location);
const fix = { coords: { latitude: 6.9271, longitude: 79.8612, accuracy: 50 }, timestamp: 1 } as Location.LocationObject;

beforeEach(() => jest.clearAllMocks());

it("asks for the foreground permission and never the background one", async () => {
  location.requestForegroundPermissionsAsync.mockResolvedValue({ granted: true } as Location.LocationPermissionResponse);
  expect(await nativeLocation.requestPermission()).toBe(true);

  location.requestForegroundPermissionsAsync.mockResolvedValue({ granted: false } as Location.LocationPermissionResponse);
  expect(await nativeLocation.requestPermission()).toBe(false);

  expect(location.requestBackgroundPermissionsAsync).not.toHaveBeenCalled();
});

it("reads a recent position, at most five minutes old", async () => {
  location.getLastKnownPositionAsync.mockResolvedValue(fix);

  expect(await nativeLocation.lastKnown()).toEqual({ latitude: 6.9271, longitude: 79.8612 });
  expect(location.getLastKnownPositionAsync).toHaveBeenCalledWith({ maxAge: 300_000 });
});

it("says null when the phone has no recent position", async () => {
  location.getLastKnownPositionAsync.mockResolvedValue(null);

  expect(await nativeLocation.lastKnown()).toBeNull();
});

it("takes a new fix at balanced accuracy and keeps only the two coordinates", async () => {
  location.getCurrentPositionAsync.mockResolvedValue(fix);

  expect(await nativeLocation.current()).toEqual({ latitude: 6.9271, longitude: 79.8612 });
  expect(location.getCurrentPositionAsync).toHaveBeenCalledWith({ accuracy: 3 });
});
