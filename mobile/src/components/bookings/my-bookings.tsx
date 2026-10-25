import { router, useFocusEffect } from "expo-router";
import { useCallback, useState } from "react";
import { Modal, Pressable, ScrollView, StyleSheet, View } from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";

import { useSession } from "@/auth/session-provider";
import { useBookingApi } from "@/bookings/api";
import { colomboToday, daysBetween } from "@/bookings/calendar";
import { bookingErrorKey } from "@/bookings/errors";
import { splitBookings } from "@/bookings/mine";
import { inLocale } from "@/bookings/names";
import type { BookingStatus, DonorBooking } from "@/bookings/types";
import { useDates } from "@/bookings/use-dates";
import { Button } from "@/components/button";
import { Card, FormError } from "@/components/screen";
import { Text } from "@/components/text";
import { TextField } from "@/components/text-field";
import { useI18n } from "@/i18n/i18n-provider";
import { radius, spacing, type Palette } from "@/theme/colors";
import { usePalette } from "@/theme/use-theme";

import { EmptyState, Failed, Loading } from "./states";

/** booking.Bookings.CancelReason's length (plan 03, E16). */
const MAX_REASON = 300;

/**
 * The donor's bookings (plan 03, 5.7): what is coming, soonest first, and what has been, latest
 * first. Each says where and when, what became of it, and why if it did not go ahead. Cancelling
 * is offered only where the API says the donor may still do it (`canCancel`, Q1).
 */
export function MyBookings() {
  const { t } = useI18n();
  const session = useSession();

  if (session.status === "loading") return <Loading />;
  if (session.status !== "authenticated") {
    return (
      <Card>
        <Text>{t("Booking.gate.donorSignIn")}</Text>
        <Button label={t("Booking.gate.signIn")} onPress={() => router.push("/sign-in")} />
        <Button label={t("Booking.gate.createAccount")} variant="secondary" onPress={() => router.push("/sign-up")} />
      </Card>
    );
  }
  if (session.activeInstitution) return <Text>{t("Booking.gate.institutionSession")}</Text>;

  return <BookingList />;
}

function BookingList() {
  const { t } = useI18n();
  const api = useBookingApi();
  const [bookings, setBookings] = useState<DonorBooking[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [cancelling, setCancelling] = useState<DonorBooking | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const load = useCallback(async () => {
    setFailed(false);
    const result = await api.listMine();
    if (result.ok) setBookings(result.value);
    else setFailed(true);
  }, [api]);

  // Every time the tab is shown: a booking just made on an institution's page belongs here.
  useFocusEffect(
    useCallback(() => {
      void load();
    }, [load]),
  );

  if (failed) return <Failed message={t("MyBookings.loadFailed")} onRetry={() => void load()} />;
  if (bookings === null) return <Loading />;

  if (bookings.length === 0) {
    return (
      <>
        <EmptyState message={t("MyBookings.none")} />
        <Button label={t("MyBookings.find")} onPress={() => router.navigate("/")} />
      </>
    );
  }

  const today = colomboToday();
  const { upcoming, past } = splitBookings(bookings, today);

  return (
    <>
      {notice && (
        <Text accessibilityLiveRegion="polite" variant="label">
          {notice}
        </Text>
      )}
      <Section title={t("MyBookings.upcoming")} empty={t("MyBookings.noneUpcoming")} bookings={upcoming} today={today} onCancel={setCancelling} />
      {past.length > 0 && <Section title={t("MyBookings.past")} bookings={past} today={today} onCancel={setCancelling} />}

      {cancelling && (
        <CancelSheet
          booking={cancelling}
          onClose={() => setCancelling(null)}
          onCancelled={() => {
            setNotice(t("MyBookings.cancelSheet.done", { bookingNo: cancelling.bookingNo }));
            setCancelling(null);
            void load();
          }}
        />
      )}
    </>
  );
}

function Section({
  title,
  empty,
  bookings,
  today,
  onCancel,
}: {
  title: string;
  empty?: string;
  bookings: DonorBooking[];
  today: string;
  onCancel: (booking: DonorBooking) => void;
}) {
  return (
    <View style={styles.section}>
      <Text variant="heading" accessibilityRole="header">
        {title}
      </Text>
      {bookings.length === 0 ? (
        <Text variant="muted">{empty}</Text>
      ) : (
        bookings.map((booking) => <BookingCard key={booking.bookingNo} booking={booking} today={today} onCancel={() => onCancel(booking)} />)
      )}
    </View>
  );
}

function BookingCard({ booking, today, onCancel }: { booking: DonorBooking; today: string; onCancel: () => void }) {
  const { t, language } = useI18n();
  const dates = useDates();
  const palette = usePalette();
  const away = daysBetween(today, booking.date);
  const live = booking.status === "Requested" || booking.status === "Approved";
  const institution = inLocale(language, booking.institutionName, booking.institutionNameSi, booking.institutionNameTa);

  return (
    <View testID={`booking-${booking.bookingNo}`} style={[styles.card, { backgroundColor: palette.surface, borderColor: palette.hairline }]}>
      {/* The stub: the day, large, as on a temple's own dana calendar. */}
      <View style={[styles.stub, { backgroundColor: live ? palette.subtle : palette.canvas, borderColor: palette.hairline }]}>
        <Text variant="muted">{dates.weekday(booking.date, "short")}</Text>
        <Text variant="title" style={{ color: live ? palette.brand : palette.muted }}>
          {Number(booking.date.slice(8))}
        </Text>
        <Text variant="muted">{dates.monthShort(booking.date)}</Text>
      </View>

      <View style={styles.body}>
        <Text variant="label">
          {inLocale(language, booking.slotName, booking.slotNameSi, booking.slotNameTa)} · {booking.startTime}
        </Text>
        <StatusBadge status={booking.status} />
        <Pressable
          accessibilityRole="link"
          onPress={() => router.push({ pathname: "/i/[slug]", params: { slug: booking.institution } })}
          style={styles.link}
        >
          <Text variant="label" style={{ color: palette.primary }}>
            {institution}
          </Text>
        </Pressable>
        <Text variant="muted">
          {live && away >= 0 && away <= 7 ? `${t("MyBookings.when", { days: away })} · ` : ""}
          {t("MyBookings.people", { count: booking.headcount })} · {t(`Booking.fulfilment.${booking.fulfilmentMode}`)} · {booking.bookingNo}
        </Text>
        {booking.inMemoryOf && <Text style={styles.memory}>{t("MyBookings.inMemoryOf", { name: booking.inMemoryOf })}</Text>}

        {booking.status === "Rejected" && booking.rejectReason && (
          <View style={[styles.reason, { backgroundColor: palette.dangerSoft }]}>
            <Text>{t("MyBookings.rejectedBecause", { reason: booking.rejectReason })}</Text>
          </View>
        )}
        {booking.status === "Cancelled" && booking.cancelReason && (
          <View style={[styles.reason, { backgroundColor: palette.subtle }]}>
            <Text>{t("MyBookings.cancelledBecause", { reason: booking.cancelReason })}</Text>
          </View>
        )}
        {booking.status === "Requested" && <Text variant="muted">{t("MyBookings.waiting")}</Text>}

        {booking.canCancel && <Button label={t("MyBookings.cancel")} variant="secondary" onPress={onCancel} />}
      </View>
    </View>
  );
}

function tone(status: BookingStatus, palette: Palette): { background: string; border: string; text: string } {
  switch (status) {
    case "Approved":
      return { background: palette.primary, border: palette.primary, text: palette.onPrimary };
    case "Requested":
      return { background: palette.subtle, border: palette.accent, text: palette.fg };
    case "Rejected":
      return { background: palette.dangerSoft, border: palette.danger, text: palette.fg };
    case "Completed":
      return { background: palette.successSoft, border: palette.success, text: palette.fg };
    default:
      return { background: palette.subtle, border: palette.hairline, text: palette.muted };
  }
}

/** A booking's status in words; the colour only repeats what the words say. */
function StatusBadge({ status }: { status: BookingStatus }) {
  const { t } = useI18n();
  const palette = usePalette();
  const colours = tone(status, palette);
  return (
    <View style={[styles.badge, { backgroundColor: colours.background, borderColor: colours.border }]}>
      <Text variant="muted" style={{ color: colours.text }}>
        {t(`Booking.status.${status}`)}
      </Text>
    </View>
  );
}

function CancelSheet({ booking, onClose, onCancelled }: { booking: DonorBooking; onClose: () => void; onCancelled: () => void }) {
  const { t } = useI18n();
  const api = useBookingApi();
  const palette = usePalette();
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);

  async function submit() {
    setBusy(true);
    setFailure(null);
    const result = await api.cancel(booking.bookingNo, reason.trim() || null);
    setBusy(false);
    if (result.ok) onCancelled();
    else setFailure(t(bookingErrorKey(result.failure)));
  }

  return (
    <Modal visible animationType="slide" presentationStyle="pageSheet" onRequestClose={onClose}>
      <SafeAreaView edges={["top", "bottom"]} style={[styles.fill, { backgroundColor: palette.canvas }]}>
        <ScrollView contentContainerStyle={styles.sheet} keyboardShouldPersistTaps="handled">
          <Text variant="title" accessibilityRole="header" style={{ color: palette.brand }}>
            {t("MyBookings.cancelSheet.title")}
          </Text>
          <Text variant="muted">{t("MyBookings.cancelSheet.description", { bookingNo: booking.bookingNo })}</Text>
          <TextField
            label={t("MyBookings.cancelSheet.reason")}
            help={t("MyBookings.cancelSheet.reasonHint")}
            value={reason}
            onChangeText={setReason}
            maxLength={MAX_REASON}
            multiline
            numberOfLines={3}
          />
          <FormError message={failure} />
          <Button label={t("MyBookings.cancelSheet.confirm")} busy={busy} onPress={() => void submit()} />
          <Button label={t("MyBookings.cancelSheet.keep")} variant="secondary" disabled={busy} onPress={onClose} />
        </ScrollView>
      </SafeAreaView>
    </Modal>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  sheet: { padding: spacing.md, gap: spacing.md, flexGrow: 1 },
  section: { gap: spacing.sm },
  card: { flexDirection: "row", borderWidth: 1, borderRadius: radius.md, overflow: "hidden" },
  stub: { width: 72, alignItems: "center", justifyContent: "center", paddingVertical: spacing.md, borderRightWidth: 1, borderStyle: "dashed" },
  body: { flex: 1, padding: spacing.md, gap: spacing.xs },
  badge: { alignSelf: "flex-start", borderWidth: 1, borderRadius: 999, paddingHorizontal: spacing.sm, paddingVertical: 2 },
  link: { minHeight: 44, justifyContent: "center", alignSelf: "flex-start" },
  memory: { fontStyle: "italic" },
  reason: { borderRadius: radius.sm, padding: spacing.sm },
});
