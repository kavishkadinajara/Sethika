"use client";

import { useTranslations } from "next-intl";
import { useId, useSyncExternalStore, type ReactNode } from "react";
import {
  applyThemePreference,
  readThemePreference,
  subscribeTheme,
  themePreferences,
  type ThemePreference,
} from "@/components/theme/theme";
import { cn } from "@/lib/utils";
import { AutoThemeIcon, MoonIcon, SunIcon } from "./icons";

const icons: Record<ThemePreference, ReactNode> = {
  system: <AutoThemeIcon className="size-4.5" />,
  light: <SunIcon className="size-4.5" />,
  dark: <MoonIcon className="size-4.5" />,
};

/**
 * Auto / Light / Dark. Auto (the default) follows the device. Native radio buttons, so arrow
 * keys, form semantics and screen-reader announcements come from the browser. The change is
 * instant (no reload) and remembered in a cookie.
 */
export function ThemeSwitcher({ className }: { className?: string }) {
  const t = useTranslations("ThemeSwitcher");
  const name = useId();
  // The server cannot know the choice, so it renders "system"; React then re-renders with the
  // real value after hydration, without a mismatch error. The page colours are already right
  // by then: the boot script set them before the first paint.
  const preference = useSyncExternalStore(subscribeTheme, readThemePreference, () => "system");

  return (
    <fieldset className={className}>
      <legend className="sr-only">{t("label")}</legend>
      {/* On a phone, three equal columns across the width: wrapping the pills instead left
          "Dark" alone on a second line at 320 px, and Sinhala's longer words made it worse.
          The icons are decoration, so they give way to the words below sm. */}
      <div className="grid w-full grid-cols-3 gap-1 rounded-3xl bg-subtle p-1 sm:inline-flex sm:w-auto sm:max-w-full sm:flex-wrap sm:rounded-full">
        {themePreferences.map((option) => (
          <label
            key={option}
            className={cn(
              "relative inline-flex min-h-11 min-w-0 cursor-pointer items-center justify-center gap-2 rounded-full px-1.5 py-1.5 text-center sm:min-w-11 sm:px-4",
              "text-sm leading-snug font-medium sm:text-base text-fg transition-colors hover:bg-surface hover:text-brand",
              "has-checked:bg-primary has-checked:text-on-primary has-checked:shadow-[0_1px_2px_rgb(14_42_33/0.3)]",
              // The same offering glow as every other control, drawn on the visible pill.
              "has-focus-visible:outline-2 has-focus-visible:outline-offset-2 has-focus-visible:outline-brand",
              "has-focus-visible:ring-6 has-focus-visible:ring-accent/40",
            )}
          >
            <input
              type="radio"
              name={name}
              value={option}
              checked={preference === option}
              onChange={() => applyThemePreference(option)}
              className="sr-only"
            />
            <span className="hidden shrink-0 sm:inline-flex">{icons[option]}</span>
            <span>{t(option)}</span>
          </label>
        ))}
      </div>
    </fieldset>
  );
}
