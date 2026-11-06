import * as SecureStore from "expo-secure-store";
import * as SystemUI from "expo-system-ui";
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { Appearance, useColorScheme } from "react-native";

import { colors } from "./colors";

/*  Auto / Light / Dark, the web's ThemeSwitcher on a phone. "auto" follows the phone. The choice
    is handed to React Native's Appearance, so useColorScheme() and the native parts (tab bar,
    sheets, keyboard) all follow it without each screen asking. Kept like the language: tiny, not
    a secret, in SecureStore because it is already here.

    `loaded` holds the splash until a saved choice is applied, so a dark choice on a light phone
    never paints light first. The root view behind every screen follows the theme too: it is
    what shows for a moment between the splash and the first screen, and during a transition. */

export const THEMES = ["auto", "light", "dark"] as const;
export type ThemeChoice = (typeof THEMES)[number];

const KEY = "sethika.theme";

const isTheme = (value: unknown): value is ThemeChoice => THEMES.includes(value as ThemeChoice);

function apply(choice: ThemeChoice) {
  Appearance.setColorScheme?.(choice === "auto" ? "unspecified" : choice);
}

type ThemePreference = { theme: ThemeChoice; setTheme: (theme: ThemeChoice) => void; loaded: boolean };

const ThemeContext = createContext<ThemePreference>({ theme: "auto", setTheme: () => {}, loaded: false });

export function ThemePreferenceProvider({ children }: { children: ReactNode }) {
  const [theme, setThemeState] = useState<ThemeChoice>("auto");
  const [loaded, setLoaded] = useState(false);
  const canvas = colors[useColorScheme() === "dark" ? "dark" : "light"].canvas;

  useEffect(() => {
    SystemUI.setBackgroundColorAsync(canvas).catch(() => {});
  }, [canvas]);

  useEffect(() => {
    SecureStore.getItemAsync(KEY)
      .then((saved) => {
        if (!isTheme(saved)) return;
        setThemeState(saved);
        apply(saved);
      })
      .catch(() => {})
      .finally(() => setLoaded(true));
  }, []);

  const setTheme = useCallback((next: ThemeChoice) => {
    setThemeState(next);
    apply(next);
    SecureStore.setItemAsync(KEY, next).catch(() => {});
  }, []);

  const value = useMemo(() => ({ theme, setTheme, loaded }), [theme, setTheme, loaded]);
  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}

export function useThemePreference(): ThemePreference {
  return useContext(ThemeContext);
}
