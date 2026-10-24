import { Platform } from "react-native";

/*  Where the Sethika API is. Set EXPO_PUBLIC_API_BASE_URL in mobile/.env.local (git-ignored),
    for example http://192.168.1.20:5182 for a phone on the same Wi-Fi as the API. The fallback
    reaches an API on this computer from an emulator: Android's emulator sees the host as
    10.0.2.2, the iOS simulator as localhost. A real phone needs the LAN address.

    EXPO_PUBLIC_ values are inlined into the app bundle, so nothing secret may ever go here. */

const fallback = Platform.OS === "android" ? "http://10.0.2.2:5182" : "http://localhost:5182";

export const API_BASE_URL = (process.env.EXPO_PUBLIC_API_BASE_URL || fallback).replace(/\/+$/, "");

/** Sent as User-Agent; the API stores it as the session's device (auth.RefreshTokens.DeviceInfo). */
export const APP_USER_AGENT = `Sethika/0.1.0 (${Platform.OS === "ios" ? "iOS" : "Android"} ${Platform.Version})`;
