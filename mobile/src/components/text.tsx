import { StyleSheet, Text as NativeText, type TextProps } from "react-native";

import { useI18n } from "@/i18n/i18n-provider";
import { usePalette } from "@/theme/use-theme";

type Variant = "title" | "heading" | "body" | "muted" | "label" | "error";

/*  Outfit carries Latin only. Sinhala and Tamil use the phone's own font (Noto Sans Sinhala /
    Tamil on Android, the system faces on iOS) rather than mixing Outfit's Latin metrics into
    a line of another script.                                                                */
const outfit = {
  regular: "Outfit_400Regular",
  medium: "Outfit_500Medium",
  semibold: "Outfit_600SemiBold",
} as const;

export function Text({ variant = "body", style, ...props }: TextProps & { variant?: Variant }) {
  const palette = usePalette();
  const { language } = useI18n();
  const latin = language === "en";

  const weight = variant === "title" || variant === "heading" ? "semibold" : variant === "label" ? "medium" : "regular";
  const color = variant === "muted" ? palette.muted : variant === "error" ? palette.danger : palette.fg;

  return (
    <NativeText
      {...props}
      style={[
        styles[variant],
        { color },
        latin
          ? { fontFamily: outfit[weight] }
          : { fontWeight: weight === "regular" ? "400" : weight === "medium" ? "500" : "600" },
        style,
      ]}
    />
  );
}

const styles = StyleSheet.create({
  title: { fontSize: 28, lineHeight: 36 },
  heading: { fontSize: 20, lineHeight: 28 },
  body: { fontSize: 16, lineHeight: 24 },
  muted: { fontSize: 14, lineHeight: 20 },
  label: { fontSize: 15, lineHeight: 22 },
  error: { fontSize: 14, lineHeight: 20 },
});
