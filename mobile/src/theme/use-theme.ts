import { useColorScheme } from "react-native";

import { colors, type Palette } from "./colors";

/** The palette for the phone's light/dark setting (app.json: userInterfaceStyle automatic). */
export function usePalette(): Palette {
  return useColorScheme() === "dark" ? colors.dark : colors.light;
}
