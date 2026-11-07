import * as Notifications from "expo-notifications";
import * as SecureStore from "expo-secure-store";
import type { Platform } from "react-native";

/*  What push.ts's phone really says to Expo. The module reads Expo Go once at load, so each test
    loads its own copy. Whether Android then shows the push is for the real phone to prove.    */

let mockExpoGo = false;

jest.mock("expo", () => ({ ...jest.requireActual<object>("expo"), isRunningInExpoGo: () => mockExpoGo }));
jest.mock("expo-constants", () => ({ __esModule: true, default: { expoConfig: { version: "1.2.3" } } }));
jest.mock("expo-secure-store", () => ({
  AFTER_FIRST_UNLOCK_THIS_DEVICE_ONLY: "this-device-only",
  getItemAsync: jest.fn(async () => "kept"),
  setItemAsync: jest.fn(async () => {}),
  deleteItemAsync: jest.fn(async () => {}),
}));
jest.mock("expo-notifications", () => ({
  AndroidImportance: { DEFAULT: 4, HIGH: 6 },
  setNotificationHandler: jest.fn(),
  setNotificationChannelAsync: jest.fn(async () => null),
  getPermissionsAsync: jest.fn(async () => ({ granted: false, canAskAgain: false, status: "denied", expires: "never" })),
  requestPermissionsAsync: jest.fn(async () => ({ granted: true, canAskAgain: true, status: "granted", expires: "never" })),
  getExpoPushTokenAsync: jest.fn(async () => ({ type: "expo", data: "ExponentPushToken[real]" })),
}));

function load(os: typeof Platform.OS = "android"): typeof import("./native-device") {
  let loaded: typeof import("./native-device") | undefined;
  jest.isolateModules(() => {
    jest.requireActual<{ Platform: typeof Platform }>("react-native").Platform.OS = os;
    loaded = jest.requireActual<typeof import("./native-device")>("./native-device");
  });
  return loaded!;
}

beforeEach(() => {
  mockExpoGo = false;
  jest.mocked(Notifications.setNotificationHandler).mockClear();
});

it("creates the bookings channel, loud enough for a banner", async () => {
  const { nativeDevice } = load();

  await nativeDevice.createChannel("Bookings");

  expect(Notifications.setNotificationChannelAsync).toHaveBeenCalledWith("bookings", { name: "Bookings", importance: 6 });
});

it("gives the permission's two answers", async () => {
  const { nativeDevice } = load();

  await expect(nativeDevice.getPermission()).resolves.toEqual({ granted: false, canAskAgain: false });
  await expect(nativeDevice.requestPermission()).resolves.toEqual({ granted: true, canAskAgain: true });
});

it("gives the Expo token itself, not its wrapper", async () => {
  await expect(load().nativeDevice.getToken()).resolves.toBe("ExponentPushToken[real]");
});

it("names the platform as the API's check spells it", () => {
  expect(load("android").nativeDevice.platform).toBe("Android");
  expect(load("ios").nativeDevice.platform).toBe("iOS");
});

it("sends the app's version", () => {
  expect(load().nativeDevice.appVersion).toBe("1.2.3");
});

it("keeps its values on this device only", async () => {
  const { nativeMemory } = load();
  const onlyHere = { keychainAccessible: "this-device-only" };

  await expect(nativeMemory.get("k")).resolves.toBe("kept");
  await nativeMemory.set("k", "v");
  await nativeMemory.delete("k");

  expect(SecureStore.getItemAsync).toHaveBeenCalledWith("k", onlyHere);
  expect(SecureStore.setItemAsync).toHaveBeenCalledWith("k", "v", onlyHere);
  expect(SecureStore.deleteItemAsync).toHaveBeenCalledWith("k", onlyHere);
});

it("shows a push that arrives while the app is open", async () => {
  load();

  const handler = jest.mocked(Notifications.setNotificationHandler).mock.calls[0]?.[0];

  await expect(handler?.handleNotification({} as Notifications.Notification)).resolves.toEqual({
    shouldShowBanner: true,
    shouldShowList: true,
    shouldPlaySound: true,
    shouldSetBadge: false,
  });
});

it("is not supported in Expo Go, and sets nothing up there", () => {
  mockExpoGo = true;

  expect(load().nativeDevice.supported).toBe(false);
  expect(Notifications.setNotificationHandler).not.toHaveBeenCalled();
});

it("is supported in a development build", () => {
  expect(load().nativeDevice.supported).toBe(true);
});
