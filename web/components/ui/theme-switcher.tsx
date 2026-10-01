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
      <div className="inline-flex max-w-full flex-wrap gap-1 rounded-full bg-subtle p-1">
        {themePreferences.map((option) => (
          <label
            key={option}
            className={cn(
              "relative inline-flex min-h-11 min-w-11 cursor-pointer items-center justify-center gap-2 rounded-full px-4 py-1.5",
              "text-base leading-snug font-medium text-fg transition-colors hover:bg-surface hover:text-brand",
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
            {icons[option]}
            <span>{t(option)}</span>
          </label>
        ))}
      </div>
    </fieldset>
  );
}
