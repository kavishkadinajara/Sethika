import { Pressable, StyleSheet, View } from "react-native";

import { canChoose, type DayState } from "@/bookings/availability";
import { addMonths, monthGrid } from "@/bookings/calendar";
import { useDates } from "@/bookings/use-dates";
import { Text } from "@/components/text";
import { useI18n } from "@/i18n/i18n-provider";
import { radius, spacing } from "@/theme/colors";
import { usePalette } from "@/theme/use-theme";

import { Bowl } from "./bowl";

export type DayView = {
  state: DayState;
  /** The whole day in words, for screen readers: the date, then what it holds. */
  label: string;
};

type Props = {
  /** "yyyy-MM". */
  month: string;
  onMonthChange: (month: string) => void;
  canGoBack: boolean;
  canGoForward: boolean;
  selected: string | null;
  /** Called for any day of the month, open or not: a closed day is chosen to hear why. */
  onSelect: (date: string) => void;
  /** Called for every day of the month, never for the neighbouring months' days. */
  day: (date: string) => DayView;
  /** The month's data is still coming: the grid shows, quiet. */
  loading?: boolean;
};

/**
 * A month as a grid of days, Sunday first: the mobile twin of the web's BowlCalendar (plan 03,
 * E32), not a calendar library. It runs edge to edge, so on a 320 pt phone each of the seven days
 * is a 45 pt target. Open days carry a bowl (empty, or filling as sponsors book); a full day a full
 * bowl and a struck-through date; a day that is not open, the plain date. Every day also says
 * itself in words to a screen reader, and the screen shows the words of the day chosen.
 */
export function BowlCalendar({ month, onMonthChange, canGoBack, canGoForward, selected, onSelect, day, loading = false }: Props) {
  const { t } = useI18n();
  const dates = useDates();
  const palette = usePalette();
  const weeks = monthGrid(month);

  return (
    <View style={styles.calendar}>
      <View style={styles.header}>
        <Text variant="heading" accessibilityRole="header" accessibilityLiveRegion="polite" style={{ color: palette.brand }}>
          {dates.monthYear(`${month}-01`)}
        </Text>
        <View style={styles.arrows}>
          <Arrow label={t("Booking.calendar.previous")} glyph="‹" enabled={canGoBack} onPress={() => onMonthChange(addMonths(month, -1))} />
          <Arrow label={t("Booking.calendar.next")} glyph="›" enabled={canGoForward} onPress={() => onMonthChange(addMonths(month, 1))} />
        </View>
      </View>

      <View style={styles.week} accessibilityElementsHidden importantForAccessibility="no-hide-descendants">
        {weeks[0].map((cell) => (
          <Text key={cell.date} variant="muted" style={styles.weekday}>
            {dates.weekday(cell.date, "narrow")}
          </Text>
        ))}
      </View>

      <View style={[styles.grid, loading && styles.quiet]}>
        {weeks.map((week) => (
          <View key={week[0].date} style={styles.week}>
            {week.map((cell) =>
              cell.inMonth ? (
                <Day key={cell.date} date={cell.date} view={day(cell.date)} selected={cell.date === selected} onPress={() => onSelect(cell.date)} />
              ) : (
                <View key={cell.date} style={styles.cell} />
              ),
            )}
          </View>
        ))}
      </View>
    </View>
  );
}

function Day({ date, view, selected, onPress }: { date: string; view: DayView; selected: boolean; onPress: () => void }) {
  const palette = usePalette();
  const { state } = view;
  const open = canChoose(state);
  const full = state.kind === "full";

  return (
    <Pressable
      testID={`day-${date}`}
      accessibilityRole="button"
      accessibilityLabel={view.label}
      accessibilityState={{ disabled: !open, selected }}
      onPress={onPress}
      style={({ pressed }) => [
        styles.cell,
        styles.day,
        selected && { backgroundColor: palette.subtle, borderColor: palette.primary, borderWidth: 2 },
        pressed && { backgroundColor: palette.subtle },
      ]}
    >
      <Text
        variant={open ? "label" : "body"}
        style={[styles.number, { color: open ? palette.fg : palette.muted }, full && styles.struck]}
      >
        {Number(date.slice(8))}
      </Text>
      <View style={styles.mark}>
        {(state.kind === "open" || state.kind === "filling") && <Bowl fill={state.fill} color={palette.brand} />}
        {full && <Bowl fill={1} color={palette.muted} />}
      </View>
    </Pressable>
  );
}

function Arrow({ label, glyph, enabled, onPress }: { label: string; glyph: string; enabled: boolean; onPress: () => void }) {
  const palette = usePalette();
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={label}
      accessibilityState={{ disabled: !enabled }}
      disabled={!enabled}
      onPress={onPress}
      style={({ pressed }) => [
        styles.arrow,
        { borderColor: palette.line, backgroundColor: pressed ? palette.subtle : palette.surface, opacity: enabled ? 1 : 0.4 },
      ]}
    >
      <Text variant="heading" style={{ color: palette.primary }}>
        {glyph}
      </Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  // Edge to edge: the screen's padding is given back, so seven days share the whole width.
  calendar: { marginHorizontal: -spacing.md, gap: spacing.sm },
  header: { flexDirection: "row", alignItems: "center", justifyContent: "space-between", paddingHorizontal: spacing.md, gap: spacing.sm },
  arrows: { flexDirection: "row", gap: spacing.sm },
  arrow: { width: 44, height: 44, borderRadius: radius.sm, borderWidth: 1, alignItems: "center", justifyContent: "center" },
  grid: { gap: 2 },
  quiet: { opacity: 0.4 },
  week: { flexDirection: "row" },
  weekday: { flex: 1, textAlign: "center" },
  cell: { flex: 1, minHeight: 56 },
  day: { alignItems: "center", justifyContent: "flex-start", paddingTop: 6, borderRadius: radius.sm, borderWidth: 2, borderColor: "transparent" },
  number: { fontSize: 16, lineHeight: 22 },
  struck: { textDecorationLine: "line-through" },
  mark: { minHeight: 18, justifyContent: "center" },
});
