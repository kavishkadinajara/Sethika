import { useCallback, useEffect, useState, type ReactNode } from "react";
import { Modal, Pressable, ScrollView, StyleSheet, View } from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";
import { WebView } from "react-native-webview";
import type { ShouldStartLoadRequest } from "react-native-webview/lib/WebViewTypes";

import { useSession } from "@/auth/session-provider";
import { formatLkr, localName } from "@/bookings/names";
import type { PublicInstitution } from "@/bookings/types";
import { Loading } from "@/components/bookings/states";
import { Button } from "@/components/button";
import { Icon } from "@/components/icon";
import { FormError } from "@/components/screen";
import { Text } from "@/components/text";
import { TextField } from "@/components/text-field";
import { useGivingApi } from "@/giving/api";
import { checkoutHtml, givingErrorKey, parseGift, payHereReturn, RETURN_CHECK_MS, RETURN_CHECKS, returnState } from "@/giving/gift";
import type { CheckoutForm, MyPayment } from "@/giving/types";
import { useI18n } from "@/i18n/i18n-provider";
import { spacing } from "@/theme/colors";
import { usePalette } from "@/theme/use-theme";

type Props = {
  institution: PublicInstitution;
  onClose: () => void;
  /** The donor chose to sign in (or sign up) first; the page opens this sheet again afterwards. */
  onSignIn: (screen: "/sign-in" | "/sign-up") => void;
};

type Stage = { step: "form" } | { step: "checkout"; form: CheckoutForm } | { step: "result"; orderId: string; left: boolean };

/**
 * Giving money online from the phone (plan 09, M8; PHASE-F-MOBILE §5.8). How much and the details
 * PayHere requires; the API writes the order and signs PayHere's form; the donor pays on PayHere's
 * own page inside the app; PayHere's signed notification to the API is what records the gift, and
 * the result asks the API for it. The merchant's secret never comes near the phone.
 */
export function GiveSheet({ institution, onClose, onSignIn }: Props) {
  const { t, language } = useI18n();
  const palette = usePalette();
  const [stage, setStage] = useState<Stage>({ step: "form" });
  const place = localName(language, institution);

  // Leaving PayHere's page before it sends the donor back is leaving the payment, never paying it.
  const leaveCheckout = (form: CheckoutForm) => setStage({ step: "result", orderId: form.orderId, left: true });
  const close = () => (stage.step === "checkout" ? leaveCheckout(stage.form) : onClose());

  return (
    <Modal visible animationType="slide" presentationStyle="pageSheet" onRequestClose={close}>
      <SafeAreaView edges={["top", "bottom"]} style={[styles.fill, { backgroundColor: palette.canvas }]}>
        <View style={[styles.handle, { backgroundColor: palette.hairline }]} />
        <View style={styles.header}>
          <Text variant="title" accessibilityRole="header" style={styles.grow}>
            {stage.step === "checkout" ? t("Giving.give.checkoutTitle") : t("Giving.give.title", { name: place })}
          </Text>
          <Pressable
            accessibilityRole="button"
            accessibilityLabel={t("Common.close")}
            onPress={close}
            hitSlop={8}
            style={({ pressed }) => [styles.close, { backgroundColor: pressed ? palette.hairline : palette.subtle }]}
          >
            <Icon name="close" size={18} color={palette.fg} />
          </Pressable>
        </View>

        {stage.step === "checkout" ? (
          <Checkout
            form={stage.form}
            onBack={(left) => setStage({ step: "result", orderId: stage.form.orderId, left })}
          />
        ) : (
          <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
            {stage.step === "form" ? (
              <>
                <Text variant="muted">{t("Giving.give.description", { name: place })}</Text>
                <Gate onSignIn={onSignIn}>
                  <GiveForm slug={institution.slug} onCheckout={(form) => setStage({ step: "checkout", form })} onClose={onClose} />
                </Gate>
              </>
            ) : (
              <GiftResult orderId={stage.orderId} left={stage.left} place={place} onClose={onClose} />
            )}
          </ScrollView>
        )}
      </SafeAreaView>
    </Modal>
  );
}

/** Giving needs the donor's own session; say how to get one, and come back here. */
function Gate({ onSignIn, children }: { onSignIn: Props["onSignIn"]; children: ReactNode }) {
  const { t } = useI18n();
  const session = useSession();

  if (session.status === "loading") return <Loading />;
  if (session.status !== "authenticated") {
    return (
      <View style={styles.block}>
        <Text>{t("Giving.give.signInFirst")}</Text>
        <Button label={t("Discover.sheet.signIn")} onPress={() => onSignIn("/sign-in")} />
        <Button label={t("Discover.sheet.createAccount")} variant="secondary" onPress={() => onSignIn("/sign-up")} />
      </View>
    );
  }
  if (session.activeInstitution) return <Text>{t("Giving.give.institutionSession")}</Text>;
  return <>{children}</>;
}

function GiveForm({ slug, onCheckout, onClose }: { slug: string; onCheckout: (form: CheckoutForm) => void; onClose: () => void }) {
  const { t } = useI18n();
  const api = useGivingApi();
  const [amount, setAmount] = useState("");
  const [email, setEmail] = useState("");
  const [address, setAddress] = useState("");
  const [city, setCity] = useState("");
  const [invalid, setInvalid] = useState<Set<string>>(new Set());
  const [failure, setFailure] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit() {
    setFailure(null);
    const value = parseGift(amount);
    const missing = new Set<string>();
    if (value === null) missing.add("amount");
    if (email.trim() === "") missing.add("email");
    if (address.trim() === "") missing.add("address");
    if (city.trim() === "") missing.add("city");
    setInvalid(missing);
    if (value === null || missing.size > 0) return;

    setBusy(true);
    const result = await api.startGift(slug, { amount: value, need: null, email: email.trim(), address: address.trim(), city: city.trim() });
    setBusy(false);

    if (result.ok) return onCheckout(result.value);
    if (result.failure.code === "invalid_details" && result.failure.fields.length > 0) return setInvalid(new Set(result.failure.fields));
    setFailure(t(givingErrorKey(result.failure)));
  }

  const error = (field: string, key: Parameters<typeof t>[0]) => (invalid.has(field) ? t(key) : null);

  return (
    <View style={styles.block}>
      <TextField
        testID="gift-amount"
        label={t("Giving.give.amount")}
        help={t("Giving.give.amountHint")}
        error={error("amount", "Giving.give.amountInvalid")}
        value={amount}
        onChangeText={setAmount}
        keyboardType="decimal-pad"
      />
      <Text variant="muted">{t("Giving.give.detailsHint")}</Text>
      <TextField
        testID="gift-email"
        label={t("Giving.give.email")}
        error={error("email", "Giving.give.emailInvalid")}
        value={email}
        onChangeText={setEmail}
        keyboardType="email-address"
        autoCapitalize="none"
        autoComplete="email"
        maxLength={256}
      />
      <TextField
        testID="gift-address"
        label={t("Giving.give.address")}
        error={error("address", "Giving.give.addressInvalid")}
        value={address}
        onChangeText={setAddress}
        autoComplete="street-address"
        maxLength={200}
      />
      <TextField
        testID="gift-city"
        label={t("Giving.give.city")}
        error={error("city", "Giving.give.cityInvalid")}
        value={city}
        onChangeText={setCity}
        maxLength={100}
      />
      <FormError message={failure} />
      <Button label={t("Giving.give.save")} busy={busy} onPress={() => void submit()} />
      <Button label={t("Giving.give.cancel")} variant="secondary" onPress={onClose} />
    </View>
  );
}

/**
 * PayHere's checkout inside the app. The page posts the API's signed form as it loads; every page
 * after that is PayHere's own (cards, bank, 3-D Secure). The moment PayHere sends the donor back to
 * Sethika's return or cancel address, the WebView stops there and the app shows the result itself.
 */
function Checkout({ form, onBack }: { form: CheckoutForm; onBack: (left: boolean) => void }) {
  const { t } = useI18n();
  const palette = usePalette();
  const [ready, setReady] = useState(false);
  const [broken, setBroken] = useState(false);
  const html = checkoutHtml(form);

  const navigate = (request: ShouldStartLoadRequest) => {
    const back = payHereReturn(request.url, form.orderId);
    if (!back) return true;
    onBack(back.left);
    return false;
  };

  if (!html || broken) {
    return (
      <View style={styles.content}>
        <FormError message={t("Giving.give.checkoutFailed")} />
        <Button label={t("Common.close")} variant="secondary" onPress={() => onBack(true)} />
      </View>
    );
  }

  return (
    <View style={[styles.fill, { backgroundColor: palette.surface }]}>
      <WebView
        testID="payhere-checkout"
        source={{ html }}
        originWhitelist={["*"]}
        setSupportMultipleWindows={false}
        onShouldStartLoadWithRequest={navigate}
        onLoadEnd={() => setReady(true)}
        onError={() => setBroken(true)}
        onHttpError={() => setBroken(true)}
        onRenderProcessGone={() => setBroken(true)}
        onContentProcessDidTerminate={() => setBroken(true)}
        style={styles.fill}
      />
      {!ready && (
        <View style={styles.cover}>
          <Loading />
        </View>
      )}
    </View>
  );
}

/**
 * What became of the gift, from PayHere's signed notification as the API recorded it (M9). The
 * notification can arrive a few seconds after the donor is back, so a pending order is asked
 * about again, three seconds apart, a few times; "waiting" stays honest after that.
 */
function GiftResult({ orderId, left, place, onClose }: { orderId: string; left: boolean; place: string; onClose: () => void }) {
  const { t, language } = useI18n();
  const api = useGivingApi();
  const palette = usePalette();
  const [payment, setPayment] = useState<MyPayment | null>(null);
  const [failure, setFailure] = useState<string | null>(null);
  const [checks, setChecks] = useState(0);

  const check = useCallback(async () => {
    const result = await api.getMyPayment(orderId);
    setChecks((n) => n + 1);
    if (result.ok) {
      setFailure(null);
      setPayment(result.value);
    } else setFailure(t(givingErrorKey(result.failure)));
  }, [api, orderId, t]);

  useEffect(() => {
    // Asking the API is what this view is for (as the web's return page does).
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void check();
  }, [check]);

  const waiting = payment !== null && returnState(payment.status, left) === "waiting";
  useEffect(() => {
    if (!waiting || checks >= RETURN_CHECKS) return;
    const timer = setTimeout(() => void check(), RETURN_CHECK_MS);
    return () => clearTimeout(timer);
  }, [waiting, checks, check]);

  if (!payment) {
    return failure ? (
      <View style={styles.block}>
        <FormError message={failure} />
        <Button label={t("Giving.return.checkAgain")} onPress={() => void check()} />
        <Button label={t("Common.close")} variant="secondary" onPress={onClose} />
      </View>
    ) : (
      <Loading />
    );
  }

  const state = returnState(payment.status, left);
  const name = localName(language, { name: payment.institutionName, nameSi: payment.institutionNameSi, nameTa: payment.institutionNameTa });
  const tone = state === "paid" ? palette.success : state === "waiting" ? palette.primary : palette.muted;

  return (
    <View style={[styles.block, styles.result]} accessibilityLiveRegion="polite">
      <View style={[styles.seal, { borderColor: tone, backgroundColor: palette.subtle }]}>
        <Icon name={state === "paid" ? "check" : state === "waiting" ? "time" : "info"} size={40} color={tone} />
      </View>
      {state === "paid" && (
        <>
          <Text variant="display" style={styles.centred}>
            {t("Giving.return.paidTitle")}
          </Text>
          <Text style={styles.centred}>{t("Giving.return.paidBody", { name, money: formatLkr(payment.amount) })}</Text>
          {payment.receiptNo && (
            <View style={[styles.number, { backgroundColor: palette.surface, borderColor: palette.line }]}>
              <Text variant="eyebrow">{t("Giving.return.receiptNo")}</Text>
              <Text variant="heading" selectable>
                {payment.receiptNo}
              </Text>
            </View>
          )}
        </>
      )}
      {state === "waiting" && (
        <>
          <Text variant="display" style={styles.centred}>
            {t("Giving.return.waitingTitle")}
          </Text>
          <Text style={styles.centred}>{t("Giving.return.waitingBody")}</Text>
          {checks >= RETURN_CHECKS && <Button label={t("Giving.return.checkAgain")} onPress={() => void check()} />}
        </>
      )}
      {state === "failed" && (
        <>
          <Text variant="display" style={styles.centred}>
            {t("Giving.return.failedTitle")}
          </Text>
          <Text style={styles.centred}>{t("Giving.return.failedBody", { name })}</Text>
        </>
      )}
      {state === "left" && (
        <>
          <Text variant="display" style={styles.centred}>
            {t("Giving.return.leftTitle")}
          </Text>
          <Text style={styles.centred}>{t("Giving.return.leftBody", { name })}</Text>
        </>
      )}
      <View style={styles.actions}>
        <Button label={t("Giving.return.backToPlace", { name: place })} variant="secondary" onPress={onClose} />
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  handle: { alignSelf: "center", width: 40, height: 5, borderRadius: 3, marginTop: spacing.sm },
  header: { flexDirection: "row", alignItems: "flex-start", gap: spacing.sm, padding: spacing.md, paddingBottom: spacing.sm },
  grow: { flex: 1 },
  close: { width: 44, height: 44, borderRadius: 22, alignItems: "center", justifyContent: "center" },
  content: { padding: spacing.md, paddingTop: 0, paddingBottom: spacing.xxl, gap: spacing.md, flexGrow: 1 },
  block: { gap: spacing.md },
  cover: { ...StyleSheet.absoluteFill, justifyContent: "center" },
  result: { alignItems: "center", paddingVertical: spacing.lg },
  seal: { width: 96, height: 96, borderRadius: 48, borderWidth: 2, alignItems: "center", justifyContent: "center", marginBottom: spacing.sm },
  number: { alignItems: "center", gap: 2, borderWidth: 1, borderStyle: "dashed", borderRadius: 12, paddingHorizontal: spacing.xl, paddingVertical: spacing.md },
  centred: { textAlign: "center" },
  actions: { alignSelf: "stretch", gap: spacing.sm, marginTop: spacing.sm },
});
