import { useColorScheme } from "react-native";

import { colors, lifts, type Palette } from "./colors";

/** The palette for the phone's light/dark setting (app.json: userInterfaceStyle automatic). */
export function usePalette(): Palette {
  return useColorScheme() === "dark" ? colors.dark : colors.light;
}

/** Warm depth for the current theme: put it in a card's style. */
export function useLift() {
  return useColorScheme() === "dark" ? lifts.dark : lifts.light;
}
