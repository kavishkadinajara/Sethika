import type { NativeTabsProps } from "expo-router/native-tabs";

import type { Language } from "@/i18n/translate";

import type { Palette } from "./colors";

/*  The tab bar from the palette: a surface with gold (dark) or green (light) on the selected tab
    and muted on the rest, so nothing is left to the platform's default colours. On iOS 26 and
    later the system draws the bar's background itself and ignores `backgroundColor`,
    `shadowColor` and `disableTransparentOnScrollEdge`. Outfit is Latin only (see Text).       */
export function tabBarLook(palette: Palette, language: Language) {
  const font = language === "en" ? { fontFamily: "Outfit_500Medium" } : {};
  return {
    backgroundColor: palette.surface,
    shadowColor: palette.hairline,
    disableTransparentOnScrollEdge: true,
    indicatorColor: palette.subtle,
    rippleColor: palette.subtle,
    labelStyle: { default: { ...font, color: palette.muted }, selected: { ...font, color: palette.primary } },
    iconColor: { default: palette.muted, selected: palette.primary },
  } satisfies Partial<NativeTabsProps>;
}
