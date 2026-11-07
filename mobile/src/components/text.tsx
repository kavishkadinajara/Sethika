import { StyleSheet, Text as NativeText, type TextProps } from "react-native";

import { useI18n } from "@/i18n/i18n-provider";
import { usePalette } from "@/theme/use-theme";

type Variant = "date" | "weekday" | "sentence" | "display" | "title" | "heading" | "body" | "muted" | "label" | "error" | "eyebrow" | "caption" | "voice";

/*  Outfit carries Latin only. Sinhala and Tamil use the phone's own font (Noto Sans Sinhala /
    Tamil on Android, the system faces on iOS) rather than mixing Outfit's Latin metrics into
    a line of another script.                                                                */
const outfit = {
  regular: "Outfit_400Regular",
  medium: "Outfit_500Medium",
  semibold: "Outfit_600SemiBold",
} as const;

/*  The second voice (DESIGN.md, signature 4): Fraunces italic, for the human lines (a greeting,
    a thank-you, an empty state), never on a control. It has Latin only, and a slanted Sinhala
    or Tamil face would be faked by the phone, so those languages say it upright.            */
const FRAUNCES = "Fraunces_400Regular_Italic";

/*  Sinhala and Tamil stack vowel signs above and below the base line, and at tight leading they
    run into the line above, so they get the web's 1.65 at every size (globals.css, :lang).    */
const SCRIPT_LEADING = 1.65;

export function Text({ variant = "body", style, ...props }: TextProps & { variant?: Variant }) {
  const palette = usePalette();
  const { language } = useI18n();
  const latin = language === "en";

  const weight =
    variant === "display" || variant === "title" || variant === "heading"
      ? "semibold"
      : variant === "label" || variant === "eyebrow"
        ? "medium"
        : "regular";
  const color =
    variant === "muted" || variant === "caption" || variant === "eyebrow" || variant === "voice"
      ? palette.muted
      : variant === "error"
        ? palette.danger
        : variant === "date" || variant === "weekday"
          ? palette.brand
          : palette.fg;
  const font =
    variant === "voice" || variant === "date" || variant === "weekday"
      ? latin
        ? { fontFamily: FRAUNCES }
        : { fontWeight: "400" as const }
      : latin
        ? { fontFamily: outfit[weight] }
        : { fontWeight: weight === "regular" ? ("400" as const) : weight === "medium" ? ("500" as const) : ("600" as const) };

  const fontSize = StyleSheet.flatten(style)?.fontSize ?? styles[variant].fontSize;

  return (
    <NativeText
      {...props}
      style={[
        styles[variant],
        { color },
        font,
        // Latin eyebrows are small capitals; Sinhala and Tamil have no case, so they stay as written.
        variant === "eyebrow" && latin && styles.capitals,
        !latin && { lineHeight: Math.ceil(fontSize * SCRIPT_LEADING) },
        style,
      ]}
    />
  );
}

const styles = StyleSheet.create({
  // The Day Ledger's date (F22): the human voice at its largest, the web's 1.25 leading.
  date: { fontSize: 44, lineHeight: 55 },
  weekday: { fontSize: 24, lineHeight: 32 },
  // The Day Ledger's one sentence: larger than body, not a heading.
  sentence: { fontSize: 19, lineHeight: 27 },
  display: { fontSize: 32, lineHeight: 40, letterSpacing: -0.4 },
  title: { fontSize: 28, lineHeight: 36, letterSpacing: -0.2 },
  heading: { fontSize: 20, lineHeight: 28 },
  body: { fontSize: 16, lineHeight: 24 },
  muted: { fontSize: 14, lineHeight: 20 },
  label: { fontSize: 15, lineHeight: 22 },
  error: { fontSize: 14, lineHeight: 20 },
  eyebrow: { fontSize: 13, lineHeight: 18 },
  caption: { fontSize: 13, lineHeight: 18 },
  voice: { fontSize: 17, lineHeight: 25 },
  capitals: { textTransform: "uppercase", letterSpacing: 1.2, fontSize: 12 },
});
