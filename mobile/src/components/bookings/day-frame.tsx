import { router, useFocusEffect } from "expo-router";
import { useCallback, useState } from "react";
import { Pressable, StyleSheet, View } from "react-native";

import { usePermissions } from "@/auth/permission-provider";
import { greetingName, useProfile } from "@/auth/profile-provider";
import { useSession } from "@/auth/session-provider";
import { useBookingApi } from "@/bookings/api";
import { colomboTime, colomboToday } from "@/bookings/calendar";
import { donorSentence, mayReadOwnBookings, mealsToGive, TABLE_SHOWN } from "@/bookings/donor-day";
import { inLocale } from "@/bookings/names";
import type { Sentence } from "@/bookings/today";
import type { DonorBooking } from "@/bookings/types";
import { useDates } from "@/bookings/use-dates";
import { useNow } from "@/bookings/use-now";
import { Card, SectionTitle } from "@/components/screen";
import { Text } from "@/components/text";
import { useI18n } from "@/i18n/i18n-provider";
import { spacing } from "@/theme/colors";
import { usePalette } from "@/theme/use-theme";

import { DayHeader } from "./day-header";
import { LedgerLine } from "./ledger-line";
import { StatusBadge } from "./my-bookings";

type Props = {
  /** Changes when the donor pulls to refresh: their bookings are read again. */
  refreshKey?: number;
};

/**
 * The top of Explore (F21, F22): the date, one sentence, and *Your table*. Only someone acting as
 * themselves who may read their bookings has them read. Everyone else gets the date and the
 * invitation. The donor's half unmounts on a sign-out or a switch, so what it read goes with it.
 */
export function DayFrame({ refreshKey = 0 }: Props) {
  const session = useSession();
  const { ready, can } = usePermissions();

  if (session.status === "loading" || (session.status === "authenticated" && !ready)) return <Frame sentence={null} />;
  if (mayReadOwnBookings(session, can)) return <DonorFrame refreshKey={refreshKey} />;
  return <Invitation />;
}

function Invitation() {
  const { language } = useI18n();
  const now = useNow();
  return <Frame sentence={donorSentence({ bookings: null, language, today: colomboToday(now), now: colomboTime(now) })} />;
}

function DonorFrame({ refreshKey }: Required<Props>) {
  const { t, language } = useI18n();
  const api = useBookingApi();
  const palette = usePalette();
  const now = useNow();
  const [bookings, setBookings] = useState<DonorBooking[] | null>(null);
  const [failed, setFailed] = useState(false);

  // Every time the tab is shown (a booking just made belongs here), and again on a pull.
  useFocusEffect(
    useCallback(() => {
      let current = true;
      void api.listMine().then((result) => {
        if (!current) return;
        // A failed read keeps what was already read: a lost signal is not a day with no meals.
        if (result.ok) setBookings(result.value);
        setFailed(!result.ok);
      });
      return () => {
        current = false;
      };
      // refreshKey is not read here; a change to it is what asks again.
      // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [api, refreshKey]),
  );

  const today = colomboToday(now);
  const time = colomboTime(now);
  const coming = bookings === null ? [] : mealsToGive(bookings, today, time);
  const sentence = bookings !== null || failed ? donorSentence({ bookings, language, today, now: time }) : null;

  return (
    <>
      <Frame sentence={sentence} />
      {coming.length > 0 && (
        <View style={styles.table}>
          <SectionTitle>{t("Dashboard.panels.yourTable")}</SectionTitle>
          <Card>
            {coming.slice(0, TABLE_SHOWN).map((booking, index) => (
              <TableLine key={booking.bookingNo} booking={booking} ruled={index > 0} />
            ))}
          </Card>
          {/* Under the card, not beside the title: side by side, si/ta broke the title over three lines. */}
          <Pressable accessibilityRole="link" onPress={() => router.navigate("/bookings")} style={styles.all}>
            <Text variant="label" style={{ color: palette.brand }}>
              {t("Dashboard.panels.allBookings")}
            </Text>
          </Pressable>
        </View>
      )}
    </>
  );
}

/** The parent's clock renders it again, so the date it reads here turns over with the sentence. */
function Frame({ sentence }: { sentence: Sentence | null }) {
  const { t } = useI18n();
  const dates = useDates();
  const name = greetingName(useProfile());

  const words = sentence && t(sentence.key, formatted(sentence, dates.full));
  return (
    <DayHeader
      lead={name ? t("Dashboard.welcome", { name }) : undefined}
      date={colomboToday()}
      sentence={words}
    />
  );
}

/** The sentence's values with its date in words. */
function formatted({ values }: Sentence, full: (date: string) => string) {
  return typeof values.date === "string" ? { ...values, date: full(values.date) } : values;
}

/** A meal to give. The line opens the place's page, as the place's name does on the web. */
function TableLine({ booking, ruled }: { booking: DonorBooking; ruled: boolean }) {
  const { t, language } = useI18n();
  const dates = useDates();
  const palette = usePalette();
  const meal = inLocale(language, booking.slotName, booking.slotNameSi, booking.slotNameTa);
  const place = inLocale(language, booking.institutionName, booking.institutionNameSi, booking.institutionNameTa);

  return (
    <Pressable
      accessibilityRole="link"
      accessibilityLabel={`${dates.dayMonth(booking.date)}, ${booking.startTime}. ${meal}, ${place}. ${t(`Booking.status.${booking.status}`)}`}
      onPress={() => router.push({ pathname: "/i/[slug]", params: { slug: booking.institution } })}
      style={({ pressed }) => [
        styles.line,
        ruled && { borderTopWidth: 1, borderTopColor: palette.hairline },
        pressed && { backgroundColor: palette.subtle },
      ]}
    >
      <LedgerLine date={booking.date} time={booking.startTime}>
        <Text variant="label">{meal}</Text>
        <Text variant="label" style={{ color: palette.brand }}>
          {place}
        </Text>
        <View style={styles.status}>
          <StatusBadge status={booking.status} />
        </View>
      </LedgerLine>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  table: { gap: spacing.sm },
  all: { minHeight: 44, justifyContent: "center", alignSelf: "flex-end" },
  line: { minHeight: 44, paddingVertical: spacing.sm },
  status: { flexDirection: "row" },
});
