import { router, useFocusEffect } from "expo-router";
import { useCallback, useState } from "react";
import { Modal, Pressable, ScrollView, StyleSheet, View } from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";

import { useSession } from "@/auth/session-provider";
import { useBookingApi } from "@/bookings/api";
import { colomboToday, daysBetween } from "@/bookings/calendar";
import { bookingErrorKey } from "@/bookings/errors";
import { canLetGoOwnDay, splitBookings } from "@/bookings/mine";
import { inLocale } from "@/bookings/names";
import type { BookingStatus, DonorBooking } from "@/bookings/types";
import { useDates } from "@/bookings/use-dates";
import { Button } from "@/components/button";
import { Icon } from "@/components/icon";
import { Card, FormError, SectionTitle } from "@/components/screen";
import { Text } from "@/components/text";
import { TextField } from "@/components/text-field";
import { useI18n } from "@/i18n/i18n-provider";
import { bowl, radius, spacing, type Palette } from "@/theme/colors";
import { usePalette } from "@/theme/use-theme";

import { Bowl } from "./bowl";
import { EmptyState, Failed, Loading } from "./states";

/** booking.Bookings.CancelReason's length (plan 03, E16). */
const MAX_REASON = 300;

/**
 * The donor's bookings (plan 03, 5.7): what is coming, soonest first, and what has been, latest
 * first. Each says where and when, what became of it, and why if it did not go ahead. Cancelling
 * is offered only where the API says the donor may still do it (`canCancel`, Q1). A regular day the
 * institution set up is not cancelled but let go (plan 08, K24), today included.
 */
type Props = {
  /** Changes when the donor pulls to refresh: the list is fetched again. */
  refreshKey?: number;
  /** A fetch finished, well or not: the pull-to-refresh spinner can stop. */
  onSettled?: () => void;
};

export function MyBookings({ refreshKey = 0, onSettled }: Props) {
  const { t } = useI18n();
  const session = useSession();
  const palette = usePalette();

  if (session.status === "loading") return <Loading />;
  if (session.status !== "authenticated") {
    return (
      <Card rim>
        <View style={styles.gateIcon}>
          <Bowl fill={0} color={palette.brand} scale={1.6} />
        </View>
        <Text>{t("Booking.gate.donorSignIn")}</Text>
        <Button label={t("Booking.gate.signIn")} onPress={() => router.push("/sign-in")} />
        <Button label={t("Booking.gate.createAccount")} variant="secondary" onPress={() => router.push("/sign-up")} />
      </Card>
    );
  }
  if (session.activeInstitution) return <Text>{t("Booking.gate.institutionSession")}</Text>;

  return <BookingList refreshKey={refreshKey} onSettled={onSettled} />;
}

function BookingList({ refreshKey, onSettled }: Required<Pick<Props, "refreshKey">> & Pick<Props, "onSettled">) {
  const { t } = useI18n();
  const api = useBookingApi();
  const [bookings, setBookings] = useState<DonorBooking[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [cancelling, setCancelling] = useState<DonorBooking | null>(null);
  const [lettingGo, setLettingGo] = useState<DonorBooking | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const palette = usePalette();

  const load = useCallback(async () => {
    setFailed(false);
    const result = await api.listMine();
    if (result.ok) setBookings(result.value);
    else setFailed(true);
    onSettled?.();
    // A new onSettled callback on its own must not fetch again.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [api]);

  // Every time the tab is shown (a booking just made on an institution's page belongs here),
  // and again when the donor pulls to refresh.
  useFocusEffect(
    useCallback(() => {
      void load();
      // refreshKey is not read here; a change to it is what asks again.
      // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [load, refreshKey]),
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
        <View style={[styles.notice, { backgroundColor: palette.successSoft, borderLeftColor: palette.success }]}>
          <Icon name="check" size={18} color={palette.success} />
          <Text accessibilityLiveRegion="polite" variant="label" style={styles.grow}>
            {notice}
          </Text>
        </View>
      )}
      <Section
        title={t("MyBookings.upcoming")}
        empty={t("MyBookings.noneUpcoming")}
        bookings={upcoming}
        today={today}
        onCancel={setCancelling}
        onLetGo={setLettingGo}
        featureFirst
      />
      {past.length > 0 && <Section title={t("MyBookings.past")} bookings={past} today={today} onCancel={setCancelling} onLetGo={setLettingGo} />}

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
      {lettingGo && (
        <CancelSheet
          booking={lettingGo}
          letGo
          onClose={() => setLettingGo(null)}
          onCancelled={() => {
            setNotice(t("MyBookings.skipSheet.done", { bookingNo: lettingGo.bookingNo }));
            setLettingGo(null);
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
  onLetGo,
  featureFirst = false,
}: {
  title: string;
  empty?: string;
  bookings: DonorBooking[];
  today: string;
  onCancel: (booking: DonorBooking) => void;
  onLetGo: (booking: DonorBooking) => void;
  /** The soonest booking leads, with the gold rim: the one card that matters most here. */
  featureFirst?: boolean;
}) {
  const palette = usePalette();
  return (
    <View style={styles.section}>
      <SectionTitle
        aside={
          bookings.length > 0 ? (
            <View style={[styles.count, { backgroundColor: palette.subtle }]}>
              <Text variant="caption" style={{ color: palette.fg }}>
                {bookings.length}
              </Text>
            </View>
          ) : undefined
        }
      >
        {title}
      </SectionTitle>
      {bookings.length === 0 ? (
        <Text variant="muted">{empty}</Text>
      ) : (
        bookings.map((booking, index) => (
          <BookingCard
            key={booking.bookingNo}
            booking={booking}
            today={today}
            featured={featureFirst && index === 0}
            onCancel={() => onCancel(booking)}
            onLetGo={() => onLetGo(booking)}
          />
        ))
      )}
    </View>
  );
}

function BookingCard({
  booking,
  today,
  featured,
  onCancel,
  onLetGo,
}: {
  booking: DonorBooking;
  today: string;
  featured: boolean;
  onCancel: () => void;
  onLetGo: () => void;
}) {
  const { t, language } = useI18n();
  const dates = useDates();
  const palette = usePalette();
  const away = daysBetween(today, booking.date);
  const live = booking.status === "Requested" || booking.status === "Approved";
  const institution = inLocale(language, booking.institutionName, booking.institutionNameSi, booking.institutionNameTa);

  return (
    <View
      testID={`booking-${booking.bookingNo}`}
      style={[
        styles.card,
        { backgroundColor: palette.surface, borderColor: featured ? palette.primary : palette.hairline },
        featured && styles.featured,
      ]}
    >
      {featured && <View style={[styles.rim, { backgroundColor: palette.accent }]} />}
      {/* The stub: the day, large, as on an institution's own meal calendar. A booking still
          going ahead is printed in the primary colour; one that is over, quietly. */}
      <View style={[styles.stub, { backgroundColor: live ? palette.primary : palette.subtle }]}>
        <Text variant="caption" style={{ color: live ? palette.onPrimary : palette.muted }}>
          {dates.weekday(booking.date, "short")}
        </Text>
        <Text variant="title" style={{ color: live ? palette.onPrimary : palette.muted }}>
          {Number(booking.date.slice(8))}
        </Text>
        <Text variant="caption" style={{ color: live ? palette.onPrimary : palette.muted }}>
          {dates.monthShort(booking.date)}
        </Text>
      </View>
      {/* The perforation between stub and body: two notches in the canvas colour. */}
      <View style={[styles.notch, styles.notchTop, { backgroundColor: palette.canvas, borderColor: palette.hairline }]} />
      <View style={[styles.notch, styles.notchBottom, { backgroundColor: palette.canvas, borderColor: palette.hairline }]} />

      <View style={styles.body}>
        <View style={styles.titleRow}>
          <Text variant="label" style={styles.grow}>
            {inLocale(language, booking.slotName, booking.slotNameSi, booking.slotNameTa)} · {booking.startTime}
          </Text>
          <StatusBadge status={booking.status} />
        </View>
        <Pressable
          accessibilityRole="link"
          onPress={() => router.push({ pathname: "/i/[slug]", params: { slug: booking.institution } })}
          style={styles.link}
        >
          <View style={styles.place}>
            <Icon name="place" size={15} color={palette.primary} />
            <Text variant="label" style={{ color: palette.primary }}>
              {institution}
            </Text>
          </View>
        </Pressable>
        <Text variant="muted">
          {live && away >= 0 && away <= 7 ? `${t("MyBookings.when", { days: away })} · ` : ""}
          {t("MyBookings.people", { count: booking.headcount })} · {t(`Booking.fulfilment.${booking.fulfilmentMode}`)} · {booking.bookingNo}
        </Text>
        {booking.inMemoryOf && <Text variant="voice">{t("MyBookings.inMemoryOf", { name: booking.inMemoryOf })}</Text>}
        {booking.regular && (
          <Text variant="label" style={{ color: palette.brand }}>
            {t("MyBookings.regular")}
          </Text>
        )}

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

        {booking.canCancel && (
          <View style={styles.cancel}>
            <Button label={t("MyBookings.cancel")} variant="danger" onPress={onCancel} />
          </View>
        )}
        {canLetGoOwnDay(booking, today) && (
          <View style={styles.cancel}>
            <Button label={t("MyBookings.cantGive")} variant="danger" onPress={onLetGo} />
          </View>
        )}
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
export function StatusBadge({ status }: { status: BookingStatus }) {
  const { t } = useI18n();
  const palette = usePalette();
  const colours = tone(status, palette);
  return (
    <View style={[styles.badge, { backgroundColor: colours.background, borderColor: colours.border }]}>
      <Text variant="caption" style={{ color: colours.text }}>
        {t(`Booking.status.${status}`)}
      </Text>
    </View>
  );
}

/** Cancelling a booking, or with `letGo` letting a regular day go: the same sheet, its own words and call. */
function CancelSheet({
  booking,
  letGo = false,
  onClose,
  onCancelled,
}: {
  booking: DonorBooking;
  letGo?: boolean;
  onClose: () => void;
  onCancelled: () => void;
}) {
  const { t } = useI18n();
  const sheet = letGo ? "MyBookings.skipSheet" : "MyBookings.cancelSheet";
  const api = useBookingApi();
  const palette = usePalette();
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);

  async function submit() {
    setBusy(true);
    setFailure(null);
    const result = await (letGo ? api.letGo : api.cancel)(booking.bookingNo, reason.trim() || null);
    setBusy(false);
    if (result.ok) onCancelled();
    else setFailure(t(bookingErrorKey(result.failure)));
  }

  return (
    <Modal visible animationType="slide" presentationStyle="pageSheet" onRequestClose={onClose}>
      <SafeAreaView edges={["top", "bottom"]} style={[styles.fill, { backgroundColor: palette.canvas }]}>
        <View style={[styles.handle, { backgroundColor: palette.hairline }]} />
        <ScrollView contentContainerStyle={styles.sheet} keyboardShouldPersistTaps="handled">
          <View style={[styles.warn, { backgroundColor: palette.dangerSoft }]}>
            <Icon name="calendar" size={26} color={palette.danger} />
          </View>
          <Text variant="title" accessibilityRole="header">
            {t(`${sheet}.title`)}
          </Text>
          <Text variant="muted">{t(`${sheet}.description`, { bookingNo: booking.bookingNo })}</Text>
          <TextField
            label={t(`${sheet}.reason`)}
            help={t(`${sheet}.reasonHint`)}
            value={reason}
            onChangeText={setReason}
            maxLength={MAX_REASON}
            multiline
            numberOfLines={3}
          />
          <FormError message={failure} />
          <Button label={t(`${sheet}.confirm`)} busy={busy} onPress={() => void submit()} />
          <Button label={t(`${sheet}.keep`)} variant="secondary" disabled={busy} onPress={onClose} />
        </ScrollView>
      </SafeAreaView>
    </Modal>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  grow: { flex: 1 },
  handle: { alignSelf: "center", width: 40, height: 5, borderRadius: 3, marginTop: spacing.sm },
  sheet: { padding: spacing.md, gap: spacing.md, flexGrow: 1 },
  warn: { width: 56, height: 56, borderRadius: 28, alignItems: "center", justifyContent: "center" },
  gateIcon: { alignItems: "flex-start", paddingVertical: spacing.xs },
  notice: { flexDirection: "row", alignItems: "center", gap: spacing.sm, borderLeftWidth: 4, borderRadius: radius.sm, padding: spacing.md },
  section: { gap: spacing.sm + 4 },
  count: { minWidth: 28, height: 24, borderRadius: radius.pill, paddingHorizontal: spacing.sm, alignItems: "center", justifyContent: "center" },
  card: { ...bowl, flexDirection: "row", borderWidth: 1, overflow: "hidden" },
  featured: { borderWidth: 2 },
  rim: { position: "absolute", top: 0, left: 104, right: 28, height: 3, zIndex: 1, borderBottomLeftRadius: 3, borderBottomRightRadius: 3 },
  stub: { width: 76, alignItems: "center", justifyContent: "center", paddingVertical: spacing.md },
  notch: { position: "absolute", left: 66, width: 20, height: 20, borderRadius: 10, borderWidth: 1 },
  notchTop: { top: -11 },
  notchBottom: { bottom: -11 },
  body: { flex: 1, padding: spacing.md, paddingLeft: spacing.md + 4, gap: spacing.xs },
  titleRow: { flexDirection: "row", alignItems: "flex-start", gap: spacing.sm },
  badge: { borderWidth: 1, borderRadius: radius.pill, paddingHorizontal: spacing.sm, paddingVertical: 2 },
  link: { minHeight: 44, justifyContent: "center", alignSelf: "flex-start" },
  place: { flexDirection: "row", alignItems: "center", gap: 4 },
  reason: { borderRadius: radius.sm, padding: spacing.sm },
  cancel: { marginTop: spacing.xs },
});
