import { router } from "expo-router";
import { useState, type ReactNode } from "react";
import { Modal, Pressable, ScrollView, StyleSheet, TextInput, View } from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";

import { useSession } from "@/auth/session-provider";
import { useBookingApi } from "@/bookings/api";
import { headcountLimits, readHeadcount, stepHeadcount } from "@/bookings/availability";
import { bookingErrorKey, dayIsGone } from "@/bookings/errors";
import { formatLkr, localName } from "@/bookings/names";
import type { BookingCreated, DayAvailability, FulfilmentMode, PublicInstitution, PublicMealSlot } from "@/bookings/types";
import { useDates } from "@/bookings/use-dates";
import { Button } from "@/components/button";
import { Icon } from "@/components/icon";
import { Card, FormError } from "@/components/screen";
import { Text } from "@/components/text";
import { TextField } from "@/components/text-field";
import { useI18n } from "@/i18n/i18n-provider";
import { bowl, radius, spacing } from "@/theme/colors";
import { usePalette } from "@/theme/use-theme";

import { Bowl } from "./bowl";
import { Loading } from "./states";

/** The columns' lengths (booking.Bookings), so a long entry is stopped while typing, not refused. */
const MAX_IN_MEMORY_OF = 200;
const MAX_NOTES = 500;

type Props = {
  institution: PublicInstitution;
  slot: PublicMealSlot;
  day: DayAvailability;
  onClose: () => void;
  /** A booking was made: the month's bowls change. */
  onBooked: () => void;
  /** The day went while the form was open: close, say why, and show the month as it is now. */
  onTaken: (message: string) => void;
  /** The donor chose to sign in (or sign up) first; the page comes back to this day afterwards. */
  onSignIn: (screen: "/sign-in" | "/sign-up") => void;
};

/**
 * Booking one slot-day (plan 03, 5.7): who will be fed, who provides the meal, in whose memory.
 * A sheet that slides up from the bottom, where a thumb reaches. It asks for a sign-in only here,
 * at the moment it is needed (E33). The result says what the API made of it: "Booked" when the
 * slot needs no approval, "Request sent" when the institution confirms each booking.
 */
export function BookingSheet({ institution, slot, day, onClose, onBooked, onTaken, onSignIn }: Props) {
  const { t, language } = useI18n();
  const dates = useDates();
  const palette = usePalette();
  const [done, setDone] = useState<BookingCreated | null>(null);

  return (
    <Modal visible animationType="slide" presentationStyle="pageSheet" onRequestClose={onClose}>
      <SafeAreaView edges={["top", "bottom"]} style={[styles.fill, { backgroundColor: palette.canvas }]}>
        <View style={[styles.handle, { backgroundColor: palette.hairline }]} />
        <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
          <View style={styles.header}>
            <Text variant="title" accessibilityRole="header" style={styles.grow}>
              {t("Discover.sheet.title", { meal: localName(language, slot) })}
            </Text>
            <Pressable
              accessibilityRole="button"
              accessibilityLabel={t("Discover.sheet.close")}
              onPress={onClose}
              hitSlop={8}
              style={({ pressed }) => [styles.close, { backgroundColor: pressed ? palette.hairline : palette.subtle }]}
            >
              <Icon name="close" size={18} color={palette.fg} />
            </Pressable>
          </View>

          {/* The day as a ticket: the date on its stub, what and where beside it. */}
          <View style={[styles.ticket, { backgroundColor: palette.surface, borderColor: palette.hairline }]}>
            <View style={[styles.stub, { backgroundColor: palette.primary }]}>
              <Text variant="caption" style={{ color: palette.onPrimary }}>
                {dates.weekday(day.date, "short")}
              </Text>
              <Text variant="title" style={{ color: palette.onPrimary }}>
                {Number(day.date.slice(8))}
              </Text>
              <Text variant="caption" style={{ color: palette.onPrimary }}>
                {dates.monthShort(day.date)}
              </Text>
            </View>
            <View style={styles.ticketBody}>
              <Text variant="muted">
                {dates.full(day.date)} · {slot.startTime}–{slot.endTime}
              </Text>
              <Text variant="label">{localName(language, institution)}</Text>
            </View>
          </View>

          {done ? (
            <Done result={done} onClose={onClose} />
          ) : (
            <Gate onSignIn={onSignIn}>
              <BookingForm
                slot={slot}
                day={day}
                onBooked={(result) => {
                  setDone(result);
                  onBooked();
                }}
                onTaken={onTaken}
              />
            </Gate>
          )}
        </ScrollView>
      </SafeAreaView>
    </Modal>
  );
}

/** Booking needs the donor's own session (plan 02, B1); say how to get one, and come back here. */
function Gate({ onSignIn, children }: { onSignIn: Props["onSignIn"]; children: ReactNode }) {
  const { t } = useI18n();
  const session = useSession();

  if (session.status === "loading") return <Loading />;

  if (session.status !== "authenticated") {
    return (
      <View style={styles.block}>
        <Text>{t("Discover.sheet.signInFirst")}</Text>
        <Button label={t("Discover.sheet.signIn")} onPress={() => onSignIn("/sign-in")} />
        <Button label={t("Discover.sheet.createAccount")} variant="secondary" onPress={() => onSignIn("/sign-up")} />
      </View>
    );
  }

  if (session.activeInstitution) return <Text>{t("Discover.sheet.institutionSession")}</Text>;

  return <>{children}</>;
}

function BookingForm({
  slot,
  day,
  onBooked,
  onTaken,
}: {
  slot: PublicMealSlot;
  day: DayAvailability;
  onBooked: (result: BookingCreated) => void;
  onTaken: (message: string) => void;
}) {
  const { t } = useI18n();
  const api = useBookingApi();
  const palette = usePalette();

  const limits = headcountLimits(slot, day);
  const [headcount, setHeadcount] = useState(String(limits.min));
  const [fulfilment, setFulfilment] = useState<FulfilmentMode>(slot.allowedFulfilment[0] ?? "DonorBrings");
  const [inMemoryOf, setInMemoryOf] = useState("");
  const [notes, setNotes] = useState("");
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);
  const [headcountError, setHeadcountError] = useState<string | null>(null);

  const people = readHeadcount(headcount, limits);
  const cost = slot.estimatedCostPerHead !== null && people !== null ? slot.estimatedCostPerHead * people : null;

  function step(by: number) {
    setHeadcount(stepHeadcount(headcount, by, limits));
    setHeadcountError(null);
  }

  async function submit() {
    setFailure(null);
    if (people === null) {
      setHeadcountError(t("Discover.sheet.headcountRange", { min: limits.min, max: limits.max }));
      return;
    }

    setBusy(true);
    const result = await api.book({
      mealSlot: slot.publicId,
      date: day.date,
      headcount: people,
      fulfilment,
      inMemoryOf: inMemoryOf.trim() || null,
      notes: notes.trim() || null,
    });
    setBusy(false);

    if (result.ok) {
      onBooked(result.value);
      return;
    }
    const message = t(bookingErrorKey(result.failure));
    if (dayIsGone(result.failure.code)) onTaken(message);
    else setFailure(message);
  }

  return (
    <View style={styles.block}>
      <View style={styles.field}>
        <Text variant="label">{t("Discover.sheet.headcount")}</Text>
        <Text variant="muted">{t("Discover.sheet.headcountHint", { min: limits.min, max: limits.max })}</Text>
        <View style={styles.stepper}>
          <StepButton label={t("Discover.sheet.fewer")} icon="minus" enabled={(people ?? limits.min) > limits.min} onPress={() => step(-1)} />
          <TextInput
            testID="headcount"
            accessibilityLabel={t("Discover.sheet.headcount")}
            value={headcount}
            onChangeText={(value) => {
              setHeadcount(value);
              setHeadcountError(null);
            }}
            keyboardType="number-pad"
            maxLength={4}
            style={[
              styles.count,
              { color: palette.fg, backgroundColor: palette.surface, borderColor: headcountError ? palette.danger : palette.line },
            ]}
          />
          <StepButton label={t("Discover.sheet.more")} icon="plus" enabled={(people ?? limits.min) < limits.max} onPress={() => step(1)} />
        </View>
        {headcountError && (
          <Text variant="error" accessibilityLiveRegion="polite">
            {headcountError}
          </Text>
        )}
      </View>

      {cost !== null && (
        <Card rim>
          <View style={styles.estimate}>
            <Icon name="heart" size={20} color={palette.primary} />
            <Text variant="label" style={styles.grow}>
              {t("Discover.sheet.estimate", { cost: formatLkr(cost) })}
            </Text>
          </View>
        </Card>
      )}

      <View style={styles.field} accessibilityRole="radiogroup" accessibilityLabel={t("Discover.sheet.whoBrings")}>
        <Text variant="label">{t("Discover.sheet.whoBrings")}</Text>
        {slot.allowedFulfilment.map((mode) => {
          const checked = fulfilment === mode;
          return (
            <Pressable
              key={mode}
              accessibilityRole="radio"
              accessibilityState={{ checked }}
              onPress={() => setFulfilment(mode)}
              style={({ pressed }) => [
                styles.option,
                {
                  borderColor: checked ? palette.primary : palette.hairline,
                  borderWidth: checked ? 2 : 1,
                  backgroundColor: checked ? palette.subtle : palette.surface,
                },
                pressed && styles.pressed,
              ]}
            >
              <View style={[styles.radio, { borderColor: checked ? palette.primary : palette.line }]}>
                {checked && <View style={[styles.radioDot, { backgroundColor: palette.primary }]} />}
              </View>
              <View style={styles.grow}>
                <Text variant="label">{t(`Booking.fulfilment.${mode}`)}</Text>
                <Text variant="muted">{t(`Discover.sheet.fulfilmentHint.${mode}`)}</Text>
              </View>
            </Pressable>
          );
        })}
      </View>

      <TextField
        label={t("Discover.sheet.inMemoryOf")}
        help={t("Discover.sheet.inMemoryOfHint")}
        value={inMemoryOf}
        onChangeText={setInMemoryOf}
        maxLength={MAX_IN_MEMORY_OF}
        autoCapitalize="words"
      />
      <TextField
        label={t("Discover.sheet.notes")}
        help={t("Discover.sheet.notesHint")}
        value={notes}
        onChangeText={setNotes}
        maxLength={MAX_NOTES}
        multiline
        numberOfLines={3}
      />

      <FormError message={failure} />

      <View style={styles.noteRow}>
        <Icon name={slot.requiresApproval ? "time" : "check"} size={16} color={palette.primary} />
        <Text variant="muted" style={styles.grow}>
          {slot.requiresApproval ? t("Discover.sheet.approvalNote") : t("Discover.sheet.instantNote")}
        </Text>
      </View>
      <Button
        label={slot.requiresApproval ? t("Discover.sheet.request") : t("Discover.sheet.confirm")}
        busy={busy}
        onPress={() => void submit()}
      />
    </View>
  );
}

function StepButton({ label, icon, enabled, onPress }: { label: string; icon: "plus" | "minus"; enabled: boolean; onPress: () => void }) {
  const palette = usePalette();
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={label}
      accessibilityState={{ disabled: !enabled }}
      disabled={!enabled}
      onPress={onPress}
      style={({ pressed }) => [
        styles.step,
        { backgroundColor: pressed ? palette.primaryStrong : palette.primary, opacity: enabled ? 1 : 0.35 },
        pressed && styles.pressed,
      ]}
    >
      <Icon name={icon} size={22} color={palette.onPrimary} />
    </Pressable>
  );
}

/** The booking made: a full bowl, its number on a ticket, and what happens next. */
function Done({ result, onClose }: { result: BookingCreated; onClose: () => void }) {
  const { t } = useI18n();
  const palette = usePalette();
  const confirmed = result.status === "Approved";

  return (
    <View style={[styles.block, styles.done]} accessibilityLiveRegion="polite">
      <View style={[styles.seal, { backgroundColor: palette.successSoft, borderColor: palette.success }]}>
        <Bowl fill={confirmed ? 1 : 0.5} color={palette.success} scale={2} />
      </View>
      <Text variant="display" style={styles.centred}>
        {confirmed ? t("Discover.sheet.doneConfirmed") : t("Discover.sheet.doneRequested")}
      </Text>
      <Text style={styles.centred}>{confirmed ? t("Discover.sheet.doneConfirmedNext") : t("Discover.sheet.doneRequestedNext")}</Text>

      <View style={[styles.number, { backgroundColor: palette.surface, borderColor: palette.line }]}>
        <Text variant="eyebrow">{t("Discover.sheet.bookingNo")}</Text>
        <Text variant="heading" selectable style={styles.mono}>
          {result.bookingNo}
        </Text>
      </View>

      <Text variant="voice" style={styles.centred}>
        {t("Discover.sheet.thanks")}
      </Text>
      <View style={styles.actions}>
        <Button
          label={t("Discover.sheet.seeMine")}
          icon="calendar"
          onPress={() => {
            onClose();
            router.navigate("/bookings");
          }}
        />
        <Button label={t("Discover.sheet.close")} variant="secondary" onPress={onClose} />
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  handle: { alignSelf: "center", width: 40, height: 5, borderRadius: 3, marginTop: spacing.sm },
  content: { padding: spacing.md, paddingBottom: spacing.xxl, gap: spacing.md, flexGrow: 1 },
  header: { flexDirection: "row", alignItems: "flex-start", gap: spacing.sm },
  grow: { flex: 1, gap: spacing.xs },
  close: { width: 44, height: 44, borderRadius: 22, alignItems: "center", justifyContent: "center" },
  ticket: { ...bowl, flexDirection: "row", borderWidth: 1, overflow: "hidden" },
  stub: { width: 76, alignItems: "center", justifyContent: "center", paddingVertical: spacing.md },
  ticketBody: { flex: 1, padding: spacing.md, gap: spacing.xs, justifyContent: "center" },
  block: { gap: spacing.md },
  field: { gap: spacing.xs },
  stepper: { flexDirection: "row", alignItems: "center", gap: spacing.md, marginTop: spacing.xs },
  step: { width: 52, height: 52, borderRadius: 26, alignItems: "center", justifyContent: "center" },
  count: { width: 96, minHeight: 52, borderWidth: 1, borderRadius: radius.md, textAlign: "center", fontSize: 22, fontWeight: "600" },
  estimate: { flexDirection: "row", alignItems: "center", gap: spacing.sm },
  option: { ...bowl, flexDirection: "row", alignItems: "center", gap: spacing.sm + 4, padding: spacing.md, minHeight: 56 },
  pressed: { transform: [{ scale: 0.97 }] },
  radio: { width: 22, height: 22, borderRadius: 11, borderWidth: 2, alignItems: "center", justifyContent: "center" },
  radioDot: { width: 10, height: 10, borderRadius: 5 },
  noteRow: { flexDirection: "row", alignItems: "flex-start", gap: spacing.sm },
  done: { alignItems: "center", paddingVertical: spacing.lg },
  seal: { width: 112, height: 112, borderRadius: 56, borderWidth: 2, alignItems: "center", justifyContent: "center", marginBottom: spacing.sm },
  number: { ...bowl, alignItems: "center", gap: 2, borderWidth: 1, borderStyle: "dashed", paddingHorizontal: spacing.xl, paddingVertical: spacing.md },
  mono: { letterSpacing: 1 },
  centred: { textAlign: "center" },
  actions: { alignSelf: "stretch", gap: spacing.sm, marginTop: spacing.sm },
});
