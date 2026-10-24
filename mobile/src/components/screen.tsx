import type { ReactNode } from "react";
import { Image, KeyboardAvoidingView, Platform, ScrollView, StyleSheet, View } from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";

import { useI18n } from "@/i18n/i18n-provider";
import { radius, spacing } from "@/theme/colors";
import { usePalette } from "@/theme/use-theme";

import { Text } from "./text";

/** A scrolling page on the canvas colour, clear of the notch and the keyboard. */
export function Screen({ children, edges = ["top", "bottom"] }: { children: ReactNode; edges?: ("top" | "bottom")[] }) {
  const palette = usePalette();
  return (
    <SafeAreaView edges={edges} style={[styles.fill, { backgroundColor: palette.canvas }]}>
      <KeyboardAvoidingView style={styles.fill} behavior={Platform.OS === "ios" ? "padding" : undefined}>
        <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
          {children}
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

/** The mark and name above an auth screen's title. */
export function BrandHeader() {
  const { t } = useI18n();
  return (
    <View style={styles.brand}>
      <Image source={require("@/assets/images/splash-icon.png")} style={styles.mark} accessibilityIgnoresInvertColors />
      <Text variant="heading">{t("Brand.name")}</Text>
    </View>
  );
}

/** A refused request, announced to screen readers. */
export function FormError({ message }: { message: string | null }) {
  const palette = usePalette();
  if (!message) return null;
  return (
    <View
      accessibilityRole="alert"
      accessibilityLiveRegion="assertive"
      style={[styles.error, { backgroundColor: palette.dangerSoft, borderColor: palette.danger }]}
    >
      <Text variant="error">{message}</Text>
    </View>
  );
}

/** A bordered block on the surface colour. */
export function Card({ children }: { children: ReactNode }) {
  const palette = usePalette();
  return (
    <View style={[styles.card, { backgroundColor: palette.surface, borderColor: palette.hairline }]}>{children}</View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  content: { padding: spacing.md, gap: spacing.md, flexGrow: 1 },
  brand: { flexDirection: "row", alignItems: "center", gap: spacing.sm, marginBottom: spacing.sm },
  mark: { width: 40, height: 40 },
  error: { borderWidth: 1, borderRadius: radius.sm, padding: spacing.md },
  card: { borderWidth: 1, borderRadius: radius.md, padding: spacing.md, gap: spacing.sm },
});
