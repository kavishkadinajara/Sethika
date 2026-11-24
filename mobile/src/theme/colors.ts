import type { ViewStyle } from "react-native";

/*  The web's semantic colour tokens (web/app/globals.css), with the same values, so a screen
    looks like the same product on a phone and in a browser. Contrast was measured there for
    both themes. colors.test.ts reads that file and fails if the two differ, so change a value
    in both places or neither. Dark is true black with Offering Gold as primary (plan 05, R1). */

export const colors = {
  light: {
    canvas: "#f7f4ec",
    surface: "#fffdf8",
    subtle: "#efe9da",
    hairline: "#e2dbc9",
    line: "#74877d",
    fg: "#0e2a21",
    muted: "#4a6157",
    brand: "#16513d",
    primary: "#16513d",
    primaryStrong: "#0f3d2e",
    onPrimary: "#f7f4ec",
    accent: "#e0a33e",
    accentStrong: "#c98a2a",
    onAccent: "#0e2a21",
    danger: "#b42318",
    dangerStrong: "#8f1c13",
    dangerSoft: "#fbeae7",
    onDanger: "#ffffff",
    success: "#1d6b4f",
    successSoft: "#e5f1ea",
    scrim: "rgba(14, 42, 33, 0.45)",
  },
  dark: {
    canvas: "#000000",
    surface: "#0c0c0c",
    subtle: "#161616",
    hairline: "#242424",
    line: "#6b665c",
    fg: "#f4f1e8",
    muted: "#a39e92",
    brand: "#e0a33e",
    primary: "#e0a33e",
    primaryStrong: "#f0c273",
    onPrimary: "#0a0a0a",
    accent: "#e6ae4f",
    accentStrong: "#f0c273",
    onAccent: "#0a0a0a",
    danger: "#ff9a8c",
    dangerStrong: "#ffb7ad",
    dangerSoft: "#2a1512",
    onDanger: "#2a0c08",
    success: "#86d6ae",
    successSoft: "#0f2a1f",
    scrim: "rgba(0, 0, 0, 0.72)",
  },
} as const;

export type Palette = { [K in keyof typeof colors.light]: string };

/** The brand itself, the same in both themes: for a panel where the brand is the point (DESIGN.md). */
export const brand = { estate: "#16513D", gold: "#E0A33E", ink: "#0E2A21", ivory: "#F7F4EC" } as const;

export const spacing = { xs: 4, sm: 8, md: 16, lg: 24, xl: 32, xxl: 48 } as const;

export const radius = { sm: 8, md: 12, lg: 20, pill: 999 } as const;

/*  The bowl (DESIGN.md, signature 1): things that hold or receive have gentle top corners and
    deeper bottom ones. `bowl` is for cards and sheets' blocks, `cup` for inputs and small tiles.
    Buttons stay pills.                                                                       */
export const bowl = {
  borderTopLeftRadius: 14,
  borderTopRightRadius: 14,
  borderBottomLeftRadius: 28,
  borderBottomRightRadius: 28,
} as const;

export const cup = {
  borderTopLeftRadius: 10,
  borderTopRightRadius: 10,
  borderBottomLeftRadius: 18,
  borderBottomRightRadius: 18,
} as const;

/*  Warm depth (signature 3), one level. Light: a green-tinted shadow, never grey. Dark (F20,
    plan 05 R5): a 1 px light top edge and a black shadow, the web's --shadow-lift, because a
    green shadow on black reads as mud. The edge is an inset shadow, which needs Android 10; on
    an older phone the card keeps its hairline border. Read it through useLift().             */
export const lifts = {
  light: {
    shadowColor: brand.ink,
    shadowOpacity: 0.1,
    shadowRadius: 14,
    shadowOffset: { width: 0, height: 6 },
    elevation: 3,
  },
  dark: {
    boxShadow: [
      { inset: true, offsetX: 0, offsetY: 1, blurRadius: 0, color: "rgba(255, 255, 255, 0.07)" },
      { offsetX: 0, offsetY: 1, blurRadius: 2, color: "rgba(0, 0, 0, 0.6)" },
      { offsetX: 0, offsetY: 14, blurRadius: 30, spreadDistance: -18, color: "rgba(0, 0, 0, 0.9)" },
    ],
  },
} as const satisfies Record<"light" | "dark", ViewStyle>;
