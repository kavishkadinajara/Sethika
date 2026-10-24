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

export const spacing = { xs: 4, sm: 8, md: 16, lg: 24, xl: 32 } as const;

export const radius = { sm: 8, md: 12 } as const;
