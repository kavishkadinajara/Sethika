import { isRunningInExpoGo } from "expo";
import Constants from "expo-constants";
import * as Notifications from "expo-notifications";
import { AndroidImportance } from "expo-notifications";
import * as SecureStore from "expo-secure-store";
import { Platform } from "react-native";

import { PUSH_CHANNEL_ID, type PushDevice, type PushMemory, type PushPermission } from "./push";

/*  The real phone behind push.ts. Asking Expo Go for a token throws on Android (F37), so
    `supported` keeps every push call away from it and the rest of the app runs there as before. */

const supported = !isRunningInExpoGo();

// As the refresh token: a backup restored onto another phone must not carry this phone's token.
const options: SecureStore.SecureStoreOptions = {
  keychainAccessible: SecureStore.AFTER_FIRST_UNLOCK_THIS_DEVICE_ONLY,
};

function permissionOf({ granted, canAskAgain }: PushPermission): PushPermission {
  return { granted, canAskAgain };
}

export const nativeDevice: PushDevice = {
  supported,
  platform: Platform.OS === "ios" ? "iOS" : "Android",
  appVersion: Constants.expoConfig?.version ?? null,
  // HIGH to match the push's own priority (F42): the default level never shows a banner.
  createChannel: (name) =>
    Notifications.setNotificationChannelAsync(PUSH_CHANNEL_ID, {
      name,
      importance: AndroidImportance.HIGH,
    }),
  getPermission: () => Notifications.getPermissionsAsync().then(permissionOf),
  requestPermission: () => Notifications.requestPermissionsAsync().then(permissionOf),
  // Reads the EAS project id from the app's config, and rejects if the build has none.
  getToken: () => Notifications.getExpoPushTokenAsync().then((token) => token.data),
};

export const nativeMemory: PushMemory = {
  get: (key) => SecureStore.getItemAsync(key, options),
  set: (key, value) => SecureStore.setItemAsync(key, value, options),
  delete: (key) => SecureStore.deleteItemAsync(key, options),
};

// With no handler set, Android drops a push that arrives while the app is open.
if (supported) {
  Notifications.setNotificationHandler({
    handleNotification: async () => ({
      shouldShowBanner: true,
      shouldShowList: true,
      shouldPlaySound: true,
      shouldSetBadge: false,
    }),
  });
}
