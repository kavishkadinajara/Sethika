import type { ReactNode } from "react";
import {
  Image,
  KeyboardAvoidingView,
  Platform,
  RefreshControl,
  ScrollView,
  StyleSheet,
  useColorScheme,
  View,
  type StyleProp,
  type ViewStyle,
} from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";

import { useI18n } from "@/i18n/i18n-provider";
import { bowl, radius, spacing } from "@/theme/colors";
import { useLift, usePalette } from "@/theme/use-theme";

import { Text } from "./text";

type ScreenProps = {
  children: ReactNode;
  edges?: ("top" | "bottom")[];
  /** Pull to refresh: shown while `refreshing`, and `onRefresh` runs when the donor pulls. */
  refreshing?: boolean;
  onRefresh?: () => void;
};

/** A scrolling page on the canvas colour, clear of the notch and the keyboard. */
export function Screen({ children, edges = ["top", "bottom"], refreshing = false, onRefresh }: ScreenProps) {
  const palette = usePalette();
  return (
    <SafeAreaView edges={edges} style={[styles.fill, { backgroundColor: palette.canvas }]}>
      <KeyboardAvoidingView style={styles.fill} behavior={Platform.OS === "ios" ? "padding" : undefined}>
        <ScrollView
          contentContainerStyle={styles.content}
          keyboardShouldPersistTaps="handled"
          refreshControl={
            onRefresh ? (
              <RefreshControl
                refreshing={refreshing}
                onRefresh={onRefresh}
                tintColor={palette.primary}
                colors={[palette.primary]}
                // Android draws the spinner on a white disc unless told otherwise.
                progressBackgroundColor={palette.surface}
              />
            ) : undefined
          }
        >
          {children}
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

/**
 * A tab's opening lines: a small eyebrow, the title, and one line in the human voice. The title
 * is the screen's header for screen readers.
 */
export function ScreenHeader({ eyebrow, title, voice, aside }: { eyebrow?: string; title: string; voice?: string; aside?: ReactNode }) {
  return (
    <View style={styles.header}>
      <View style={styles.headerText}>
        {eyebrow && <Text variant="eyebrow">{eyebrow}</Text>}
        <Text variant="display" accessibilityRole="header">
          {title}
        </Text>
        {voice && <Text variant="voice">{voice}</Text>}
      </View>
      {aside}
    </View>
  );
}

const MARK = {
  light: require("@/assets/images/splash-icon.png"),
  dark: require("@/assets/images/splash-icon-dark.png"),
};

/** The mark and name above an auth screen's title: the ivory bowl on black, the green one on ivory. */
export function BrandHeader() {
  const { t } = useI18n();
  const scheme = useColorScheme();
  return (
    <View style={styles.brand}>
      <View style={styles.brandRow}>
        <Image source={scheme === "dark" ? MARK.dark : MARK.light} style={styles.mark} accessibilityIgnoresInvertColors />
        <Text variant="heading" style={styles.wordmark}>
          {t("Brand.name")}
        </Text>
      </View>
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
      style={[styles.error, { backgroundColor: palette.dangerSoft, borderLeftColor: palette.danger }]}
    >
      <Text variant="error" style={{ color: palette.fg }}>
        {message}
      </Text>
    </View>
  );
}

/**
 * A bowl-shaped block on the surface colour with warm depth. `rim` draws the gold rim on its top
 * edge: the one card that matters most on a screen, never more than one per view (DESIGN.md).
 */
export function Card({ children, rim = false, style }: { children: ReactNode; rim?: boolean; style?: StyleProp<ViewStyle> }) {
  const palette = usePalette();
  const lift = useLift();
  return (
    <View style={[styles.card, lift, { backgroundColor: palette.surface, borderColor: palette.hairline }, style]}>
      {rim && <View style={[styles.rim, { backgroundColor: palette.accent }]} />}
      {children}
    </View>
  );
}

/** A small heading over a group of rows or cards. */
export function SectionTitle({ children, aside }: { children: string; aside?: ReactNode }) {
  return (
    <View style={styles.sectionTitle}>
      <Text variant="heading" accessibilityRole="header" style={styles.grow}>
        {children}
      </Text>
      {aside}
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  grow: { flex: 1 },
  content: { padding: spacing.md, paddingBottom: spacing.xxl, gap: spacing.md, flexGrow: 1 },
  header: { flexDirection: "row", alignItems: "flex-start", gap: spacing.md, paddingTop: spacing.sm },
  headerText: { flex: 1, gap: 2 },
  brand: { gap: spacing.xs, marginBottom: spacing.sm },
  brandRow: { flexDirection: "row", alignItems: "center", gap: spacing.sm },
  mark: { width: 44, height: 44 },
  wordmark: { letterSpacing: 0.3 },
  error: { borderLeftWidth: 4, borderRadius: radius.sm, padding: spacing.md },
  card: { ...bowl, borderWidth: 1, padding: spacing.md, gap: spacing.sm, overflow: "visible" },
  // The web's rim: a short line near the left of the top edge, not a bar across it (DESIGN.md).
  rim: { position: "absolute", top: -1, left: 20, width: 48, height: 3, borderBottomLeftRadius: 3, borderBottomRightRadius: 3 },
  sectionTitle: { flexDirection: "row", alignItems: "center", gap: spacing.sm, marginTop: spacing.sm },
});
