import { Pressable, StyleSheet, View } from "react-native";

import { canChoose, type DayState } from "@/bookings/availability";
import { addMonths, monthGrid } from "@/bookings/calendar";
import { useDates } from "@/bookings/use-dates";
import { Icon } from "@/components/icon";
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
  /** "yyyy-MM-dd" in Colombo: that day carries a small gold mark under its number. */
  today?: string;
};

/**
 * A month as a grid of days, Sunday first: the mobile twin of the web's BowlCalendar (plan 03,
 * E32), not a calendar library. It runs edge to edge, so on a 320 pt phone each of the seven days
 * is a 45 pt target. Open days carry a bowl (empty, or filling as sponsors book); a full day a full
 * bowl and a struck-through date; a day that is not open, the plain date. Every day also says
 * itself in words to a screen reader, and the screen shows the words of the day chosen.
 */
export function BowlCalendar({ month, onMonthChange, canGoBack, canGoForward, selected, onSelect, day, loading = false, today }: Props) {
  const { t } = useI18n();
  const dates = useDates();
  const weeks = monthGrid(month);

  return (
    <View style={styles.calendar}>
      <View style={styles.header}>
        <Text variant="heading" accessibilityRole="header" accessibilityLiveRegion="polite" style={styles.month}>
          {dates.monthYear(`${month}-01`)}
        </Text>
        <View style={styles.arrows}>
          <Arrow label={t("Booking.calendar.previous")} icon="back" enabled={canGoBack} onPress={() => onMonthChange(addMonths(month, -1))} />
          <Arrow label={t("Booking.calendar.next")} icon="forward" enabled={canGoForward} onPress={() => onMonthChange(addMonths(month, 1))} />
        </View>
      </View>

      <View style={styles.week} accessibilityElementsHidden importantForAccessibility="no-hide-descendants">
        {weeks[0].map((cell) => (
          <Text key={cell.date} variant="caption" style={styles.weekday}>
            {dates.weekday(cell.date, "narrow")}
          </Text>
        ))}
      </View>

      <View style={[styles.grid, loading && styles.quiet]}>
        {weeks.map((week) => (
          <View key={week[0].date} style={styles.week}>
            {week.map((cell) =>
              cell.inMonth ? (
                <Day
                  key={cell.date}
                  date={cell.date}
                  view={day(cell.date)}
                  selected={cell.date === selected}
                  isToday={cell.date === today}
                  onPress={() => onSelect(cell.date)}
                />
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

function Day({
  date,
  view,
  selected,
  isToday,
  onPress,
}: {
  date: string;
  view: DayView;
  selected: boolean;
  isToday: boolean;
  onPress: () => void;
}) {
  const palette = usePalette();
  const { state } = view;
  const open = canChoose(state);
  const full = state.kind === "full";
  // A chosen open day is filled in the primary colour, its bowl drawn in the colour on it.
  const filled = selected && open;
  const ink = filled ? palette.onPrimary : open ? palette.fg : palette.muted;

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
        open && !filled && { backgroundColor: palette.canvas },
        selected && !filled && { borderColor: palette.primary },
        filled && { backgroundColor: palette.primary, borderColor: palette.primary },
        pressed && !filled && { backgroundColor: palette.subtle },
      ]}
    >
      <Text variant={open ? "label" : "body"} style={[styles.number, { color: ink }, full && styles.struck]}>
        {Number(date.slice(8))}
      </Text>
      <View style={styles.mark}>
        {(state.kind === "open" || state.kind === "filling") && <Bowl fill={state.fill} color={filled ? palette.onPrimary : palette.brand} />}
        {full && <Bowl fill={1} color={palette.muted} />}
      </View>
      {isToday && <View style={[styles.today, { backgroundColor: filled ? palette.onPrimary : palette.accent }]} />}
    </Pressable>
  );
}

function Arrow({ label, icon, enabled, onPress }: { label: string; icon: "back" | "forward"; enabled: boolean; onPress: () => void }) {
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
      <Icon name={icon} size={18} color={palette.primary} />
    </Pressable>
  );
}

const styles = StyleSheet.create({
  // Edge to edge: the page puts the calendar on a full-width band, so seven days share the whole width.
  calendar: { gap: spacing.sm },
  header: { flexDirection: "row", alignItems: "center", justifyContent: "space-between", paddingHorizontal: spacing.md, gap: spacing.sm },
  arrows: { flexDirection: "row", gap: spacing.sm },
  month: { flex: 1 },
  arrow: { width: 44, height: 44, borderRadius: radius.pill, borderWidth: 1, alignItems: "center", justifyContent: "center" },
  grid: { gap: 4, paddingHorizontal: 2 },
  quiet: { opacity: 0.4 },
  week: { flexDirection: "row", gap: 2 },
  weekday: { flex: 1, textAlign: "center" },
  cell: { flex: 1, minHeight: 56 },
  day: { alignItems: "center", justifyContent: "flex-start", paddingTop: 6, borderRadius: radius.md, borderWidth: 2, borderColor: "transparent" },
  number: { fontSize: 16, lineHeight: 22 },
  struck: { textDecorationLine: "line-through" },
  mark: { minHeight: 18, justifyContent: "center" },
  today: { position: "absolute", bottom: 4, width: 14, height: 3, borderRadius: 2 },
});
