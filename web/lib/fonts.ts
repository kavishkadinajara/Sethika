import { Fraunces, Noto_Sans_Sinhala, Noto_Sans_Tamil, Outfit } from "next/font/google";

/*  A10: Outfit has no Sinhala or Tamil glyphs. The stacks in globals.css put Outfit first
    and the Noto families after it. Every face carries a unicode-range, so Latin text matches
    Outfit and Sinhala/Tamil text skips it and lands on Noto.

    The Noto families load only their own script subset and no metric fallback: a fallback
    face is local Arial with no unicode-range, and it would sit in the stack ahead of the
    system fonts for no benefit. They are not preloaded either - an English page never
    downloads them, because the browser fetches a face only when a glyph in its range appears. */

export const outfit = Outfit({
  variable: "--font-outfit",
  subsets: ["latin"],
});

// One static weight, not the variable font: the tagline voice is only ever regular italic,
// and this halves the file every page preloads.
export const fraunces = Fraunces({
  variable: "--font-fraunces",
  subsets: ["latin"],
  style: "italic",
  weight: "400",
});

export const notoSinhala = Noto_Sans_Sinhala({
  variable: "--font-noto-sinhala",
  subsets: ["sinhala"],
  preload: false,
  adjustFontFallback: false,
});

export const notoTamil = Noto_Sans_Tamil({
  variable: "--font-noto-tamil",
  subsets: ["tamil"],
  preload: false,
  adjustFontFallback: false,
});

export const fontVariables = [outfit, fraunces, notoSinhala, notoTamil]
  .map((font) => font.variable)
  .join(" ");
