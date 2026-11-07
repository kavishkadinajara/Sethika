import { StyleSheet, View } from "react-native";

import { useDates } from "@/bookings/use-dates";
import { Text } from "@/components/text";
import { radius, spacing } from "@/theme/colors";
import { usePalette } from "@/theme/use-theme";

/**
 * The Day Ledger's frame (F21, F22), on Explore and on Today: who is here, the date large in the
 * human voice, and the one sentence that says what today needs. `sentence` is null while its data
 * loads, and shows as a quiet bar rather than words that may turn out wrong.
 */
export function DayHeader({ lead, date, sentence }: { lead?: string; date: string; sentence: string | null }) {
  const dates = useDates();
  const palette = usePalette();

  return (
    <View style={[styles.header, { borderBottomColor: palette.hairline }]}>
      {lead && <Text variant="muted">{lead}</Text>}
      <View accessible accessibilityRole="header" accessibilityLabel={dates.full(date)}>
        <Text variant="weekday">{dates.weekday(date, "long")}</Text>
        <Text variant="date">{dates.dayMonthLong(date)}</Text>
      </View>
      {sentence === null ? (
        <View testID="sentence-waiting" style={[styles.waiting, { backgroundColor: palette.subtle }]} />
      ) : (
        // Polite: the sentence changes when the day does, and that is worth hearing.
        <Text variant="sentence" accessibilityLiveRegion="polite">
          {sentence}
        </Text>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  header: { gap: spacing.xs, paddingTop: spacing.sm, paddingBottom: spacing.md, borderBottomWidth: 1 },
  waiting: { height: 27, width: "75%", borderRadius: radius.pill },
});
