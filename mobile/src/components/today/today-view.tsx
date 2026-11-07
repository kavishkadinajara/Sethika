import { useFocusEffect } from "expo-router";
import { useCallback, useRef, useState, type ReactNode } from "react";
import { Linking, Pressable, StyleSheet, View } from "react-native";

import { usePermissions } from "@/auth/permission-provider";
import { useSession } from "@/auth/session-provider";
import { useBookingApi, type BookingApi, type BookingResult } from "@/bookings/api";
import { addDays, colomboTime } from "@/bookings/calendar";
import type { BookingFailure } from "@/bookings/errors";
import { inLocale } from "@/bookings/names";
import {
  CALENDAR_APPROVE,
  CALENDAR_REJECT,
  countRequests,
  failureKey,
  institutionSentence,
  mayOpenToday,
  mayServe,
  movedOn,
  todayMeals,
  WAITING_DAYS,
  type TodayMeal,
} from "@/bookings/today";
import type { CalendarBooking, SlotDay, TodaysTable } from "@/bookings/types";
import { useDates } from "@/bookings/use-dates";
import { Bowl } from "@/components/bookings/bowl";
import { StatusBadge } from "@/components/bookings/my-bookings";
import { EmptyState, Failed, Loading } from "@/components/bookings/states";
import { Button } from "@/components/button";
import { Icon } from "@/components/icon";
import { Card, FormError } from "@/components/screen";
import { Text } from "@/components/text";
import { useI18n } from "@/i18n/i18n-provider";
import { radius, spacing } from "@/theme/colors";
import { usePalette } from "@/theme/use-theme";

type Props = {
  /** Changing it reads the day again (pull to refresh). */
  refreshKey?: number;
  /** Called when a read ends, so the pull's spinner can stop. */
  onSettled?: () => void;
};

type Day = { table: TodaysTable; days: SlotDay[]; waiting: number | null };

/**
 * The institution's Day Ledger on a phone (F23): the sentence, then each meal as a bowl with its
 * sponsors, and Served on a confirmed booking (F24). The tab is hidden from anyone it is not for,
 * but a link can still land here, so it asks again. The API's check is the security.
 */
export function TodayView({ refreshKey = 0, onSettled }: Props) {
  const { t } = useI18n();
  const session = useSession();
  const { ready, can } = usePermissions();

  if (session.status === "authenticated" && !ready) return <Loading />;
  if (!mayOpenToday(session, can)) return <Text>{t("Booking.gate.notAllowed")}</Text>;

  return <Ledger refreshKey={refreshKey} onSettled={onSettled} />;
}

/** The server's date decides the day, because the phone's clock may be wrong. */
async function readDay(api: BookingApi, answers: boolean): Promise<BookingResult<Day>> {
  const table = await api.getToday();
  if (!table.ok) return table;

  const date = table.value.date;
  const [day, asked] = await Promise.all([
    api.getCalendar(date, date),
    answers ? api.getCalendar(date, addDays(date, WAITING_DAYS - 1), "Requested") : null,
  ]);
  if (!day.ok) return day;

  // The count is only part of a sentence: without it the day is still worth showing.
  return { ok: true, value: { table: table.value, days: day.value, waiting: asked?.ok ? countRequests(asked.value) : null } };
}

function Ledger({ refreshKey, onSettled }: Required<Pick<Props, "refreshKey">> & Pick<Props, "onSettled">) {
  const { t, language } = useI18n();
  const api = useBookingApi();
  const dates = useDates();
  const session = useSession();
  const { can } = usePermissions();
  const palette = usePalette();
  const answers = can(CALENDAR_APPROVE) || can(CALENDAR_REJECT);

  const [day, setDay] = useState<Day | null>(null);
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [confirming, setConfirming] = useState<string | null>(null);
  const [serving, setServing] = useState(false);
  const [refused, setRefused] = useState<{ bookingNo: string; message: string } | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const latest = useRef(0);

  const load = useCallback(async () => {
    const mine = ++latest.current;
    const result = await readDay(api, answers);
    // A slower, older read must not paint over a newer one, or undo a booking just served.
    if (mine !== latest.current) return;
    if (result.ok) {
      setDay(result.value);
      setFailure(null);
    } else {
      setFailure(result.failure);
    }
    onSettled?.();
    // A new onSettled callback on its own must not fetch again.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [api, answers]);

  // Every time the tab is shown, so a day left open overnight turns over, and on a pull.
  useFocusEffect(
    useCallback(() => {
      void load();
      // refreshKey is not read here; a change to it is what asks again.
      // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [load, refreshKey]),
  );

  async function serve(booking: CalendarBooking) {
    setServing(true);
    setRefused(null);
    setNotice(null);

    const result = await api.markServed(booking.bookingNo);
    if (result.ok) {
      setConfirming(null);
      setNotice(t("Calendar.booking.served", { bookingNo: booking.bookingNo }));
      await load();
    } else {
      setRefused({ bookingNo: booking.bookingNo, message: t(failureKey(result.failure)) });
      if (movedOn(result.failure)) {
        setConfirming(null);
        await load();
      }
    }
    setServing(false);
  }

  if (day === null) {
    if (failure) return <Failed message={t(failureKey(failure))} onRetry={() => void load()} />;
    return <Loading />;
  }

  const name =
    session.status === "authenticated"
      ? session.institutions.find((institution) => institution.slug === session.activeInstitution)?.name
      : undefined;
  const sentence = institutionSentence({ table: day.table, waiting: day.waiting, language, now: colomboTime() });
  const meals = todayMeals(day.table, day.days);

  return (
    <>
      <View style={styles.header}>
        {name && <Text variant="eyebrow">{name}</Text>}
        <Text variant="date" accessibilityRole="header" style={styles.date}>
          {dates.full(day.table.date)}
        </Text>
        <Text variant="voice">{t(sentence.key, sentence.values)}</Text>
      </View>

      {failure && <Text variant="muted">{t("Dashboard.panels.stale")}</Text>}

      {notice && (
        <View style={[styles.notice, { backgroundColor: palette.successSoft, borderLeftColor: palette.success }]}>
          <Icon name="check" size={18} color={palette.success} />
          <Text accessibilityLiveRegion="polite" variant="label" style={styles.grow}>
            {notice}
          </Text>
        </View>
      )}

      {meals.length === 0 ? (
        <EmptyState message={t("Shell.today.none")} />
      ) : (
        meals.map((meal) => (
          <Meal key={meal.mealSlot} meal={meal}>
            {meal.sponsors.map((booking) => (
              <Sponsor
                key={booking.bookingNo}
                booking={booking}
                offerServed={mayServe(booking, can)}
                confirming={confirming === booking.bookingNo}
                serving={serving}
                refusal={refused?.bookingNo === booking.bookingNo ? refused.message : null}
                onAsk={() => {
                  setRefused(null);
                  setConfirming(booking.bookingNo);
                }}
                onBack={() => setConfirming(null)}
                onConfirm={() => void serve(booking)}
              />
            ))}
          </Meal>
        ))
      )}
    </>
  );
}

function stateLine(meal: TodayMeal, t: ReturnType<typeof useI18n>["t"]): string {
  if (meal.state === "Closed") return t("Shell.today.closed");
  const places = meal.state === "Full" ? t("Shell.today.full") : t("Shell.today.places", { booked: meal.bookedSponsors, max: meal.maxSponsors });
  return meal.requested > 0 ? `${places} · ${t("Shell.today.requested", { count: meal.requested })}` : places;
}

function Meal({ meal, children }: { meal: TodayMeal; children: ReactNode }) {
  const { t, language } = useI18n();
  const palette = usePalette();

  return (
    <Card>
      <View testID={`meal-${meal.mealSlot}`} style={styles.meal}>
        <Bowl
          fill={meal.maxSponsors ? meal.bookedSponsors / meal.maxSponsors : 0}
          color={meal.state === "Closed" ? palette.line : palette.brand}
          scale={1.6}
        />
        <View style={styles.grow}>
          <Text variant="label">
            {meal.startTime} · {inLocale(language, meal.name, meal.nameSi, meal.nameTa)}
          </Text>
          <Text variant="muted">{stateLine(meal, t)}</Text>
        </View>
      </View>
      {meal.sponsors.length === 0 ? <Text variant="muted">{t("App.today.noSponsors")}</Text> : children}
    </Card>
  );
}

function Sponsor({
  booking,
  offerServed,
  confirming,
  serving,
  refusal,
  onAsk,
  onBack,
  onConfirm,
}: {
  booking: CalendarBooking;
  offerServed: boolean;
  confirming: boolean;
  serving: boolean;
  refusal: string | null;
  onAsk: () => void;
  onBack: () => void;
  onConfirm: () => void;
}) {
  const { t } = useI18n();
  const palette = usePalette();

  return (
    <View testID={`sponsor-${booking.bookingNo}`} style={[styles.sponsor, { borderTopColor: palette.hairline }]}>
      <View style={styles.sponsorHead}>
        <Text variant="label" style={styles.grow}>
          {booking.donorName}
        </Text>
        <StatusBadge status={booking.status} />
      </View>
      <Pressable
        accessibilityRole="link"
        accessibilityLabel={t("App.today.call", { name: booking.donorName })}
        onPress={() => void Linking.openURL(`tel:${booking.donorPhone}`).catch(() => {})}
        style={styles.phone}
      >
        <Text variant="label" style={{ color: palette.primary }}>
          {booking.donorPhone}
        </Text>
      </Pressable>
      <Text variant="muted">
        {t("MyBookings.people", { count: booking.headcount })} · {t(`Booking.fulfilment.${booking.fulfilmentMode}`)} · {booking.bookingNo}
      </Text>
      {booking.standing && (
        <Text variant="label" style={{ color: palette.brand }}>
          {t("Calendar.booking.standing")}
        </Text>
      )}
      {booking.inMemoryOf && <Text variant="voice">{t("MyBookings.inMemoryOf", { name: booking.inMemoryOf })}</Text>}
      {booking.notes && (
        <View style={[styles.notes, { backgroundColor: palette.subtle }]}>
          <Text>{booking.notes}</Text>
        </View>
      )}

      {offerServed &&
        (confirming ? (
          <View style={styles.confirm}>
            <Text>{t("App.today.confirm", { name: booking.donorName })}</Text>
            <Button label={t("App.today.confirmYes")} busy={serving} onPress={onConfirm} />
            <Button label={t("Calendar.booking.back")} variant="secondary" disabled={serving} onPress={onBack} />
          </View>
        ) : (
          <Button label={t("App.today.served")} variant="secondary" icon="check" disabled={serving} onPress={onAsk} />
        ))}
      <FormError message={refusal} />
    </View>
  );
}

const styles = StyleSheet.create({
  grow: { flex: 1 },
  header: { gap: spacing.xs, paddingTop: spacing.sm },
  date: { fontSize: 30, lineHeight: 40 },
  notice: { flexDirection: "row", alignItems: "center", gap: spacing.sm, borderLeftWidth: 3, borderRadius: radius.sm, padding: spacing.sm + 4 },
  meal: { flexDirection: "row", alignItems: "center", gap: spacing.md },
  sponsor: { gap: spacing.xs, borderTopWidth: 1, paddingTop: spacing.sm + 4 },
  sponsorHead: { flexDirection: "row", alignItems: "center", gap: spacing.sm },
  phone: { alignSelf: "flex-start", minHeight: 44, justifyContent: "center" },
  notes: { borderRadius: radius.sm, padding: spacing.sm },
  confirm: { gap: spacing.sm, marginTop: spacing.xs },
});
