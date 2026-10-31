/*  The web's semantic colour tokens (web/app/globals.css), with the same values, so a screen
    looks like the same product on a phone and in a browser. Contrast was measured there for
    both themes; change a value in both places or neither.                                   */

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
    onAccent: "#0e2a21",
    danger: "#b42318",
    dangerSoft: "#fbeae7",
    success: "#1d6b4f",
    successSoft: "#e5f1ea",
  },
  dark: {
    canvas: "#0e2a21",
    surface: "#15382d",
    subtle: "#1c4237",
    hairline: "#2a5345",
    line: "#6e9384",
    fg: "#f2eee3",
    muted: "#a9beb4",
    brand: "#82d0ac",
    primary: "#79cba5",
    primaryStrong: "#99d9bb",
    onPrimary: "#0b231c",
    accent: "#e6ae4f",
    onAccent: "#0e2a21",
    danger: "#ff9a8c",
    dangerSoft: "#3d1f1a",
    success: "#86d6ae",
    successSoft: "#183f32",
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

/** Warm depth (signature 3): one level, a green-tinted shadow, never grey. */
export const lift = {
  shadowColor: brand.ink,
  shadowOpacity: 0.1,
  shadowRadius: 14,
  shadowOffset: { width: 0, height: 6 },
  elevation: 3,
} as const;
