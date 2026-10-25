import { router } from "expo-router";
import { useCallback, useEffect, useState, type ReactNode } from "react";
import { Linking, Pressable, StyleSheet, View } from "react-native";

import { useSession } from "@/auth/session-provider";
import { useBookingApi } from "@/bookings/api";
import { canChoose, dayState, dayWords, slotDays } from "@/bookings/availability";
import { addMonths, colomboToday, maskDays, monthOf, monthWindow } from "@/bookings/calendar";
import { bookingErrorKey, type BookingFailure } from "@/bookings/errors";
import { formatLkr, inLocale, localName } from "@/bookings/names";
import { bookingReturnPath } from "@/bookings/return-path";
import type { Availability, DayAvailability, PublicInstitution, PublicMealSlot } from "@/bookings/types";
import { useDates } from "@/bookings/use-dates";
import { Button } from "@/components/button";
import { Icon, type IconName } from "@/components/icon";
import { FormError, SectionTitle } from "@/components/screen";
import { Text } from "@/components/text";
import { useI18n } from "@/i18n/i18n-provider";
import { bowl, cup, radius, spacing } from "@/theme/colors";
import { usePalette } from "@/theme/use-theme";

import { Bowl } from "./bowl";
import { BowlCalendar, type DayView } from "./bowl-calendar";
import { BookingSheet } from "./booking-sheet";
import { EmptyState, Failed, Loading } from "./states";

type Props = {
  slug: string;
  /** A meal and day named in the route: a shared link, or the donor back from signing in (E33). */
  chosen: { slot: string; date: string } | null;
};

/**
 * An institution's own page (plan 03, C10), open to anyone: who they are, the meals they offer,
 * and a month of the chosen meal's days as bowls. Choosing an open day opens the booking sheet;
 * booking asks for a sign-in there, and the donor comes back to this meal and day.
 */
export function InstitutionPage({ slug, chosen }: Props) {
  const { t, language } = useI18n();
  const api = useBookingApi();
  const session = useSession();
  const dates = useDates();
  const palette = usePalette();

  const [institution, setInstitution] = useState<PublicInstitution | null>(null);
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [slotId, setSlotId] = useState<string | null>(chosen?.slot ?? null);
  const [month, setMonth] = useState(() => monthOf(chosen?.date ?? colomboToday()));
  const [months, setMonths] = useState<Record<string, Availability>>({});
  const [monthFailed, setMonthFailed] = useState(false);
  // The day last tapped, open or not: its words show under the calendar.
  const [inspected, setInspected] = useState<string | null>(chosen?.date ?? null);
  // The day the sheet is open for, as it was when chosen: the sheet keeps it while the month
  // refreshes under it, so a booking's confirmation is not lost when the booking fills the day.
  const [sheetDay, setSheetDay] = useState<DayAvailability | null>(null);
  // A day the donor left for sign-in from; the sheet opens on it again once they are signed in.
  const [awaitingSignIn, setAwaitingSignIn] = useState<DayAvailability | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  // The route's meal and day: opened once it is loaded, and again whenever the route names
  // another (the donor back from signing in, E33).
  const routeKey = chosen ? `${chosen.slot}|${chosen.date}` : null;
  const [handledKey, setHandledKey] = useState<string | null>(null);
  const [wanted, setWanted] = useState<{ slot: string; date: string } | null>(null);
  // Opening on a month with no open day for the meal moves on to the next one, once.
  const [movedOn, setMovedOn] = useState(chosen !== null);

  const load = useCallback(async () => {
    setFailure(null);
    const result = await api.getInstitution(slug);
    if (!result.ok) {
      setFailure(result.failure);
      return;
    }
    setInstitution(result.value);
    setSlotId((current) =>
      result.value.slots.some((slot) => slot.publicId === current) ? current : (result.value.slots[0]?.publicId ?? null),
    );
  }, [api, slug]);

  const loadMonth = useCallback(
    async (wantedMonth: string) => {
      setMonthFailed(false);
      const result = await api.getAvailability(slug, wantedMonth);
      if (result.ok) setMonths((loaded) => ({ ...loaded, [wantedMonth]: result.value }));
      else setMonthFailed(true);
    },
    [api, slug],
  );

  useEffect(() => {
    // Loading the page's data is what this component is for (as on the web).
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    if (institution && !months[month]) void loadMonth(month);
  }, [institution, month, months, loadMonth]);

  // The adjustments below follow from what has loaded and what the route says. They are made
  // while rendering, React's way of deriving state from props, not in effects, which would
  // render every change twice.

  if (chosen && routeKey !== handledKey) {
    setHandledKey(routeKey);
    setWanted(chosen);
    setSlotId(chosen.slot);
    setMonth(monthOf(chosen.date));
    setInspected(chosen.date);
  }

  const slot = institution?.slots.find((s) => s.publicId === slotId) ?? null;
  const days = slot ? slotDays(months[month], slot.publicId) : undefined;

  // The route's day has loaded: open its sheet if it can still be booked.
  if (wanted && slot && slot.publicId === wanted.slot && days) {
    const day = days.get(wanted.date);
    setWanted(null);
    if (day?.bookable) setSheetDay(day);
  }

  // Back from signing in: the sheet opens again on the day the donor left it for.
  if (session.status === "authenticated" && awaitingSignIn) {
    setSheetDay(awaitingSignIn);
    setAwaitingSignIn(null);
  }

  if (!movedOn && slot && days) {
    setMovedOn(true);
    if (![...days.values()].some((day) => day.bookable)) setMonth(addMonths(month, 1));
  }

  if (failure) {
    return failure.status === 404 ? (
      <EmptyState message={t("Discover.notFound")} />
    ) : (
      <Failed message={t(bookingErrorKey(failure))} onRetry={() => void load()} />
    );
  }
  if (!institution) return <Loading />;

  const today = colomboToday();
  const window = slot ? monthWindow(today, slot.maxAdvanceDays) : null;

  /** Fetches the month again: someone may have taken a day while the donor looked at it. */
  const refresh = () => void loadMonth(month);

  function choose(date: string) {
    setInspected(date);
    setNotice(null);
    const day = days?.get(date);
    if (slot && day && canChoose(dayState(slot, day))) setSheetDay(day);
  }

  function view(date: string, meal: PublicMealSlot): DayView {
    const state = dayState(meal, days?.get(date));
    const words = dayWords(state);
    return { state, label: words ? `${dates.full(date)}. ${t(words.key, words.values)}` : dates.full(date) };
  }

  const inspectedWords = slot && inspected && monthOf(inspected) === month ? dayWords(dayState(slot, days?.get(inspected))) : null;

  return (
    <>
      <Profile institution={institution} />

      {institution.slots.length === 0 ? (
        <EmptyState message={t("Discover.noSlots")} />
      ) : (
        <>
          <SlotChooser
            slots={institution.slots}
            selected={slotId}
            onSelect={(id) => {
              setSlotId(id);
              setInspected(null);
              setNotice(null);
            }}
          />

          {slot && window && (
            <View style={styles.section}>
              <SectionTitle>{t("Discover.chooseDay")}</SectionTitle>
              <Text variant="muted">{t("Discover.chooseDayHint", { meal: localName(language, slot) })}</Text>
              <Text variant="muted">{t("App.calendar.tapHint")}</Text>

              <FormError message={notice} />

              <View style={[styles.band, { backgroundColor: palette.surface, borderColor: palette.hairline }]}>
                <BowlCalendar
                  month={month}
                  onMonthChange={(next) => {
                    setMonth(next);
                    setInspected(null);
                  }}
                  canGoBack={month > window.first}
                  canGoForward={month < window.last}
                  selected={inspected}
                  onSelect={choose}
                  loading={!days && !monthFailed}
                  day={(date) => view(date, slot)}
                  today={today}
                />

                <View style={styles.bandBody}>
                  {inspected && inspectedWords && (
                    <View style={[styles.dayStatus, { backgroundColor: palette.subtle }]}>
                      <Icon name="calendar" size={16} color={palette.primary} />
                      <Text accessibilityLiveRegion="polite" style={styles.grow}>
                        {t("App.calendar.dayStatus", { date: dates.full(inspected), status: t(inspectedWords.key, inspectedWords.values) })}
                      </Text>
                    </View>
                  )}
                  {monthFailed && <Failed message={t("Discover.monthFailed")} onRetry={refresh} />}
                  <Legend shared={slot.bookingMode === "Shared"} />
                </View>
              </View>
            </View>
          )}

          {slot && sheetDay && (
            <BookingSheet
              institution={institution}
              slot={slot}
              day={sheetDay}
              onClose={() => setSheetDay(null)}
              onBooked={refresh}
              onTaken={(message) => {
                setSheetDay(null);
                setNotice(message);
                refresh();
              }}
              onSignIn={(screen) => {
                const day = sheetDay;
                setSheetDay(null);
                setAwaitingSignIn(day);
                router.push({ pathname: screen, params: { next: bookingReturnPath(slug, slot.publicId, day.date) } });
              }}
            />
          )}
        </>
      )}
    </>
  );
}

function Profile({ institution }: { institution: PublicInstitution }) {
  const { t, language } = useI18n();
  const palette = usePalette();
  const name = localName(language, institution);
  const address = [institution.addressLine1, institution.addressLine2, institution.city].filter(Boolean).join(", ");

  return (
    <View style={styles.profile}>
      <View style={[styles.monogram, { backgroundColor: palette.subtle, borderColor: palette.hairline }]}>
        <Text variant="title" style={{ color: palette.brand }}>
          {Array.from(name)[0] ?? ""}
        </Text>
      </View>
      <Text variant="eyebrow">{inLocale(language, institution.typeName, institution.typeNameSi, institution.typeNameTa)}</Text>
      <Text variant="display" accessibilityRole="header">
        {name}
      </Text>
      <View style={styles.place}>
        <Icon name="place" size={16} />
        <Text variant="muted" style={styles.grow}>
          {address} · {inLocale(language, institution.district, institution.districtSi, institution.districtTa)}
        </Text>
      </View>
      {institution.description && <Text style={styles.description}>{institution.description}</Text>}
      {institution.dietaryNotes && (
        <View style={[styles.note, { backgroundColor: palette.subtle, borderColor: palette.hairline }]}>
          <Icon name="info" size={18} color={palette.primary} />
          <Text style={styles.grow}>
            <Text variant="label">{t("Discover.dietary")}</Text> {institution.dietaryNotes}
          </Text>
        </View>
      )}
      <View style={styles.directions}>
        <Button
          label={t("App.institution.directions")}
          variant="quiet"
          icon="map"
          onPress={() =>
            void Linking.openURL(
              `https://www.google.com/maps/search/?api=1&query=${institution.latitude},${institution.longitude}`,
            ).catch(() => {})
          }
        />
      </View>
    </View>
  );
}

/** The meals on offer as a choice of cards; each says what sponsoring it means. */
function SlotChooser({ slots, selected, onSelect }: { slots: PublicMealSlot[]; selected: string | null; onSelect: (id: string) => void }) {
  const { t, language } = useI18n();
  const dates = useDates();
  const palette = usePalette();

  return (
    <View style={styles.section} accessibilityRole="radiogroup" accessibilityLabel={t("Discover.chooseMeal")}>
      <SectionTitle>{t("Discover.chooseMeal")}</SectionTitle>
      {slots.map((slot) => {
        const checked = slot.publicId === selected;
        const days = maskDays(slot.daysOfWeekMask);
        return (
          <Pressable
            key={slot.publicId}
            testID={`slot-${slot.publicId}`}
            accessibilityRole="radio"
            accessibilityState={{ checked }}
            onPress={() => onSelect(slot.publicId)}
            style={({ pressed }) => [
              styles.slot,
              {
                borderColor: checked ? palette.primary : palette.hairline,
                backgroundColor: checked ? palette.subtle : palette.surface,
                borderWidth: checked ? 2 : 1,
              },
              pressed && styles.pressed,
            ]}
          >
            <View style={styles.slotTitle}>
              <View style={[styles.slotIcon, { backgroundColor: checked ? palette.primary : palette.subtle }]}>
                <Icon name="meal" size={20} color={checked ? palette.onPrimary : palette.primary} />
              </View>
              <View style={styles.grow}>
                <Text variant="heading">{localName(language, slot)}</Text>
                <Text variant="muted">
                  {slot.startTime}–{slot.endTime} ·{" "}
                  {days.length === 7 ? t("Discover.everyDay") : days.map((day) => dates.weekdayName(day, "short")).join(" ")}
                </Text>
              </View>
              <View style={[styles.radio, { borderColor: checked ? palette.primary : palette.line }]}>
                {checked && <View style={[styles.radioDot, { backgroundColor: palette.primary }]} />}
              </View>
            </View>
            <View style={styles.facts}>
              <Fact
                icon="people"
                words={slot.bookingMode === "Shared" ? t("Discover.shared", { count: slot.maxSponsors }) : t("Discover.exclusive")}
              />
              <Fact icon="person" words={t("Discover.people", { min: slot.minHeadcount, max: slot.maxHeadcount })} />
              {slot.estimatedCostPerHead !== null && (
                <Fact icon="heart" words={t("Discover.costPerHead", { cost: formatLkr(slot.estimatedCostPerHead) })} />
              )}
            </View>
          </Pressable>
        );
      })}
    </View>
  );
}

/** One fact about a meal, as a small tag: an icon and the words that say it. */
function Fact({ icon, words }: { icon: IconName; words: string }) {
  const palette = usePalette();
  return (
    <View style={[styles.fact, { backgroundColor: palette.canvas, borderColor: palette.hairline }]}>
      <Icon name={icon} size={14} />
      <Text variant="caption" style={{ color: palette.fg }}>
        {words}
      </Text>
    </View>
  );
}

/** What the bowls mean, in words: the picture is never the only signal. */
function Legend({ shared }: { shared: boolean }) {
  const { t } = useI18n();
  const palette = usePalette();
  const rows: { key: string; mark: ReactNode; words: string }[] = [
    { key: "open", mark: <Bowl fill={0} color={palette.brand} />, words: t("Discover.legend.open") },
    ...(shared ? [{ key: "filling", mark: <Bowl fill={0.5} color={palette.brand} />, words: t("Discover.legend.filling") }] : []),
    { key: "full", mark: <Bowl fill={1} color={palette.muted} />, words: t("Discover.legend.full") },
    {
      key: "plain",
      mark: (
        <Text variant="muted" style={styles.plainDate}>
          7
        </Text>
      ),
      words: t("Discover.legend.notOffered"),
    },
  ];

  return (
    <View style={styles.legend}>
      {rows.map((row) => (
        <View key={row.key} style={styles.legendRow}>
          <View style={styles.legendMark}>{row.mark}</View>
          <Text variant="muted">{row.words}</Text>
        </View>
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  profile: { gap: spacing.xs },
  monogram: { ...cup, width: 64, height: 68, borderWidth: 1, alignItems: "center", justifyContent: "center", marginBottom: spacing.sm },
  place: { flexDirection: "row", alignItems: "flex-start", gap: 6, marginTop: 2 },
  description: { marginTop: spacing.sm },
  note: { ...bowl, flexDirection: "row", alignItems: "flex-start", gap: spacing.sm, borderWidth: 1, padding: spacing.md, marginTop: spacing.sm },
  directions: { alignSelf: "flex-start", marginTop: spacing.sm },
  section: { gap: spacing.sm },
  // The calendar runs edge to edge (seven 45 pt days on a 320 pt phone) on a surface band.
  band: { marginHorizontal: -spacing.md, borderTopWidth: 1, borderBottomWidth: 1, paddingVertical: spacing.md, gap: spacing.sm },
  bandBody: { paddingHorizontal: spacing.md, gap: spacing.sm },
  dayStatus: { flexDirection: "row", alignItems: "flex-start", gap: spacing.sm, borderRadius: radius.md, padding: spacing.sm + 4 },
  slot: { ...bowl, padding: spacing.md, gap: spacing.sm, minHeight: 44 },
  pressed: { transform: [{ scale: 0.98 }] },
  slotTitle: { flexDirection: "row", alignItems: "center", gap: spacing.sm + 4 },
  slotIcon: { width: 40, height: 40, borderRadius: 20, alignItems: "center", justifyContent: "center" },
  grow: { flex: 1 },
  radio: { width: 22, height: 22, borderRadius: 11, borderWidth: 2, alignItems: "center", justifyContent: "center" },
  radioDot: { width: 10, height: 10, borderRadius: 5 },
  facts: { flexDirection: "row", flexWrap: "wrap", gap: spacing.xs + 2 },
  fact: {
    flexDirection: "row",
    alignItems: "center",
    gap: 4,
    borderWidth: 1,
    borderRadius: radius.pill,
    paddingHorizontal: spacing.sm + 2,
    paddingVertical: 4,
  },
  legend: { flexDirection: "row", flexWrap: "wrap", columnGap: spacing.md, rowGap: spacing.xs, marginTop: spacing.xs },
  legendRow: { flexDirection: "row", alignItems: "center", gap: spacing.xs },
  legendMark: { width: 30, alignItems: "center" },
  plainDate: { textAlign: "center" },
});
