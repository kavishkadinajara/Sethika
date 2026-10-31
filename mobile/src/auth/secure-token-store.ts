import * as SecureStore from "expo-secure-store";

import type { TokenStore } from "./session-client";

const KEY = "sethika.refreshToken";

/*  The refresh token in the iOS Keychain / Android Keystore-backed storage. AFTER_FIRST_UNLOCK so
    a refresh can run while the phone is locked (a notification opening the app), and
    THIS_DEVICE_ONLY so a backup restored onto another phone does not carry the session with it. */
const options: SecureStore.SecureStoreOptions = {
  keychainAccessible: SecureStore.AFTER_FIRST_UNLOCK_THIS_DEVICE_ONLY,
};

export const secureTokenStore: TokenStore = {
  read: () => SecureStore.getItemAsync(KEY, options),
  write: (token) => SecureStore.setItemAsync(KEY, token, options),
  clear: () => SecureStore.deleteItemAsync(KEY, options),
};
