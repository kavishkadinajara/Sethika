import type { ReactNode } from "react";
import { StyleSheet, View } from "react-native";

import { useDates } from "@/bookings/use-dates";
import { Text } from "@/components/text";
import { spacing } from "@/theme/colors";
import { usePalette } from "@/theme/use-theme";

/**
 * One line of a ledger (DESIGN.md): the day and the time in a narrow column, then what happens
 * then. `quiet` is a line that is over. Lists, not tables, so a long name wraps at 320 pt.
 */
export function LedgerLine({ date, time, quiet = false, children }: { date: string; time: string; quiet?: boolean; children: ReactNode }) {
  const dates = useDates();
  const palette = usePalette();

  return (
    <View style={styles.line}>
      <View style={styles.when}>
        <Text variant="label" style={{ color: quiet ? palette.muted : palette.fg }}>
          {dates.dayMonth(date)}
        </Text>
        <Text variant="muted" style={styles.time}>
          {time}
        </Text>
      </View>
      <View style={styles.what}>{children}</View>
    </View>
  );
}

const styles = StyleSheet.create({
  line: { flexDirection: "row", alignItems: "flex-start", gap: spacing.sm + 4 },
  when: { width: 76 },
  time: { fontVariant: ["tabular-nums"] },
  what: { flex: 1, gap: spacing.xs },
});
