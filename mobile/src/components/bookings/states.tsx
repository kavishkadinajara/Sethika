import { ActivityIndicator, StyleSheet, View } from "react-native";

import { Button } from "@/components/button";
import { FormError } from "@/components/screen";
import { Text } from "@/components/text";
import { useI18n } from "@/i18n/i18n-provider";
import { radius, spacing } from "@/theme/colors";
import { usePalette } from "@/theme/use-theme";

import { Bowl } from "./bowl";

/*  The three states every booking screen shares while it is not showing its data: loading, a
    failure with a way to try again, and empty. Kept together so they read the same everywhere. */

export function Loading() {
  const { t } = useI18n();
  const palette = usePalette();
  return (
    <View style={styles.loading} accessibilityLabel={t("Common.loading")} accessibilityRole="progressbar">
      <ActivityIndicator color={palette.primary} size="large" />
    </View>
  );
}

/** A load that failed: what happened, and a button to try again. */
export function Failed({ message, onRetry }: { message: string; onRetry: () => void }) {
  const { t } = useI18n();
  return (
    <View style={styles.failed}>
      <FormError message={message} />
      <Button label={t("Discover.retry")} variant="secondary" onPress={onRetry} />
    </View>
  );
}

/** A quiet empty state: the bowl, waiting, and one line in Sethika's own voice. */
export function EmptyState({ message }: { message: string }) {
  const palette = usePalette();
  return (
    <View style={[styles.empty, { borderColor: palette.hairline }]}>
      <Bowl fill={0} color={palette.brand} scale={1.6} />
      <Text variant="muted" style={styles.emptyText}>
        {message}
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  loading: { paddingVertical: spacing.xl, alignItems: "center" },
  failed: { gap: spacing.sm },
  empty: {
    alignItems: "center",
    gap: spacing.sm,
    borderWidth: 1,
    borderStyle: "dashed",
    borderRadius: radius.md,
    paddingVertical: spacing.xl,
    paddingHorizontal: spacing.lg,
  },
  emptyText: { textAlign: "center", fontStyle: "italic" },
});
