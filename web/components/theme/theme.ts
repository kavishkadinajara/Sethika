/*  Theme preference: "system" follows the operating system (no data-theme on <html>);
    "light" and "dark" override it. The choice lives in a cookie, not localStorage, so it is
    also sent to the server should a server render ever need it.                           */

export const themePreferences = ["system", "light", "dark"] as const;
export type ThemePreference = (typeof themePreferences)[number];

export const THEME_COOKIE = "sethika_theme";
const ONE_YEAR = 60 * 60 * 24 * 365;

/** Browser-chrome colour (address bar on phones) per theme: the canvas colour. */
export const themeColors = { light: "#f7f4ec", dark: "#000000" } as const;

/**
 * Runs in <head> before the first paint (see ThemeScript), so a saved choice never flashes
 * the other theme on a full page load. Plain ES5, no dependencies; only the two known values
 * are accepted. ThemeSync takes over after hydration.
 */
export const themeBootScript = `(function(){try{var m=document.cookie.match(/(?:^|; )${THEME_COOKIE}=(light|dark)(?:;|$)/);if(m)document.documentElement.setAttribute("data-theme",m[1])}catch(e){}})()`;

const listeners = new Set<() => void>();

export function subscribeTheme(listener: () => void) {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

export function readThemePreference(): ThemePreference {
  const value = document.documentElement.getAttribute("data-theme");
  return value === "light" || value === "dark" ? value : "system";
}

function savedPreference(): ThemePreference {
  const match = document.cookie.match(new RegExp(`(?:^|; )${THEME_COOKIE}=(light|dark)(?:;|$)`));
  return match ? (match[1] as ThemePreference) : "system";
}

/**
 * Puts the saved choice back on <html> and the theme-color metas. React 19 resets <html>'s
 * attributes when the root layout re-renders for another locale, so ThemeSync calls this
 * after every navigation, in a layout effect (before paint).
 */
export function syncThemeFromCookie() {
  const preference = savedPreference();
  const root = document.documentElement;

  if (preference === "system") root.removeAttribute("data-theme");
  else if (root.getAttribute("data-theme") !== preference) root.setAttribute("data-theme", preference);

  // The phone's address bar: with "system", each meta's own media query decides.
  for (const meta of document.querySelectorAll<HTMLMetaElement>('meta[name="theme-color"]')) {
    const own = (meta.getAttribute("media") ?? "").includes("dark") ? themeColors.dark : themeColors.light;
    meta.setAttribute("content", preference === "system" ? own : themeColors[preference]);
  }

  listeners.forEach((listener) => listener());
}

export function applyThemePreference(preference: ThemePreference) {
  document.cookie =
    preference === "system"
      ? `${THEME_COOKIE}=; path=/; max-age=0; SameSite=Lax`
      : `${THEME_COOKIE}=${preference}; path=/; max-age=${ONE_YEAR}; SameSite=Lax`;
  syncThemeFromCookie();
}
