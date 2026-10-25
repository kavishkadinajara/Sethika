import * as SecureStore from "expo-secure-store";
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { Appearance } from "react-native";

/*  Auto / Light / Dark, the web's ThemeSwitcher on a phone. "auto" follows the phone. The choice
    is handed to React Native's Appearance, so useColorScheme() and the native parts (tab bar,
    sheets, keyboard) all follow it without each screen asking. Kept like the language: tiny, not
    a secret, in SecureStore because it is already here.                                      */

export const THEMES = ["auto", "light", "dark"] as const;
export type ThemeChoice = (typeof THEMES)[number];

const KEY = "sethika.theme";

const isTheme = (value: unknown): value is ThemeChoice => THEMES.includes(value as ThemeChoice);

function apply(choice: ThemeChoice) {
  Appearance.setColorScheme?.(choice === "auto" ? "unspecified" : choice);
}

type ThemePreference = { theme: ThemeChoice; setTheme: (theme: ThemeChoice) => void };

const ThemeContext = createContext<ThemePreference>({ theme: "auto", setTheme: () => {} });

export function ThemePreferenceProvider({ children }: { children: ReactNode }) {
  const [theme, setThemeState] = useState<ThemeChoice>("auto");

  useEffect(() => {
    SecureStore.getItemAsync(KEY)
      .then((saved) => {
        if (!isTheme(saved)) return;
        setThemeState(saved);
        apply(saved);
      })
      .catch(() => {});
  }, []);

  const setTheme = useCallback((next: ThemeChoice) => {
    setThemeState(next);
    apply(next);
    SecureStore.setItemAsync(KEY, next).catch(() => {});
  }, []);

  const value = useMemo(() => ({ theme, setTheme }), [theme, setTheme]);
  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}

export function useThemePreference(): ThemePreference {
  return useContext(ThemeContext);
}
