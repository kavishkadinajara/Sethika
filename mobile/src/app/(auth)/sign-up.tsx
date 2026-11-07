import { router, useLocalSearchParams } from "expo-router";
import { useEffect, useState } from "react";
import { StyleSheet, Switch, View } from "react-native";

import { describeFailure, getJson, networkFailure, postJson } from "@/api/http";
import { leaveSignIn, passNext } from "@/auth/leave-sign-in";
import { useSessionClient } from "@/auth/session-provider";
import { Button } from "@/components/button";
import { CodeStep, PhoneStep } from "@/components/code-steps";
import { PasswordFailures } from "@/components/password-failures";
import { BrandHeader, Card, FormError, Screen } from "@/components/screen";
import { Text } from "@/components/text";
import { TextField } from "@/components/text-field";
import { useI18n } from "@/i18n/i18n-provider";
import { spacing } from "@/theme/colors";
import { usePalette } from "@/theme/use-theme";

/*  Donor signup, the same four steps as the web's SignupFlow: number, code, details, consent.
    The code buys a verification ticket (/api/auth/otp/verify); /api/auth/mobile/signup spends it
    and answers with the first session in the body.                                            */

type LegalDocument = { code: string; version: string; title: string; content: string; contentSha256: string };
type Details = { fullName: string; email: string; password: string };
type Step = "phone" | "code" | "details" | "consent";

const STEPS: Step[] = ["phone", "code", "details", "consent"];

export default function SignUpScreen() {
  const { t, language } = useI18n();
  const client = useSessionClient();
  const { next } = useLocalSearchParams<{ next?: string }>();
  const [step, setStep] = useState<Step>("phone");
  const [phone, setPhone] = useState("");
  const [ticket, setTicket] = useState<string | null>(null);
  const [details, setDetails] = useState<Details>({ fullName: "", email: "", password: "" });
  const [failure, setFailure] = useState<string | null>(null);
  const [passwordFailures, setPasswordFailures] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);

  async function run(work: () => Promise<void>) {
    setBusy(true);
    setFailure(null);
    try {
      await work();
    } catch {
      setFailure(networkFailure(t).message);
    } finally {
      setBusy(false);
    }
  }

  const requestCode = (number: string) =>
    run(async () => {
      const response = await postJson("/api/auth/otp/request", { phoneNumber: number, purpose: "signup", language });
      if (!response.ok) {
        setFailure((await describeFailure(response, t)).message);
        return;
      }
      setPhone(number);
      setStep("code");
    });

  const verify = (code: string) =>
    run(async () => {
      const response = await postJson("/api/auth/otp/verify", { phoneNumber: phone, purpose: "signup", code });
      if (!response.ok) {
        setFailure((await describeFailure(response, t)).message);
        return;
      }
      setTicket(((await response.json()) as { ticket: string }).ticket);
      setStep("details");
    });

  const create = (documents: LegalDocument[]) =>
    run(async () => {
      const response = await postJson("/api/auth/mobile/signup", {
        phoneNumber: phone,
        verificationTicket: ticket,
        fullName: details.fullName.trim(),
        email: details.email.trim() || null,
        password: details.password,
        preferredLanguage: language,
        acceptedDocumentHashes: documents.map((document) => document.contentSha256),
      });

      if (response.status === 201 && response.headers.get("content-type")?.includes("json")) {
        await client.establish(response);
        leaveSignIn(next);
        return;
      }
      if (response.status === 201) {
        // The account exists but the first sign-in did not happen (AuthEndpoints.SignUpAsync).
        router.replace({ pathname: "/sign-in", params: passNext(next) });
        return;
      }

      const refused = await describeFailure(response, t);
      setFailure(refused.message);
      if (refused.code === "password_rejected") {
        setPasswordFailures(Array.isArray(refused.body?.failures) ? (refused.body.failures as string[]) : []);
        setStep("details");
      } else if (refused.code === "phone_not_verified") {
        setTicket(null);
        setStep("phone");
      } else if (refused.code === "invalid_full_name" || refused.code === "invalid_email") {
        setStep("details");
      }
    });

  const current = STEPS.indexOf(step) + 1;

  return (
    <Screen>
      <BrandHeader />
      <Text variant="title" accessibilityRole="header">
        {t("Signup.title")}
      </Text>
      <Text variant="muted">{t("Signup.step", { current, total: STEPS.length })}</Text>

      <FormError message={failure} />

      {step === "phone" && (
        <>
          <Text variant="heading">{t("Signup.phoneTitle")}</Text>
          <PhoneStep
            label={t("Signup.phoneLabel")}
            help={t("Signup.phoneHelp")}
            requiredMessage={t("Signup.phoneRequired")}
            submitLabel={t("Signup.sendCode")}
            busy={busy}
            initial={phone}
            onSubmit={(number) => void requestCode(number)}
          />
          <Text variant="muted">{t("Signup.haveAccount")}</Text>
          {/* Signup may have been opened straight from the booking sheet, with no sign-in below it. */}
          <Button
            label={t("Signup.signIn")}
            variant="secondary"
            onPress={() => router.dismissTo({ pathname: "/sign-in", params: passNext(next) })}
          />
        </>
      )}

      {step === "code" && (
        <>
          <Text variant="heading">{t("Signup.codeTitle")}</Text>
          <Text variant="muted">{t("Signup.codeSentTo", { phone })}</Text>
          <CodeStep
            submitLabel={t("Signup.verify")}
            busy={busy}
            onSubmit={(code) => void verify(code)}
            onResend={() => void requestCode(phone)}
            onChangeNumber={() => setStep("phone")}
          />
        </>
      )}

      {step === "details" && (
        <DetailsStep
          initial={details}
          passwordFailures={passwordFailures}
          onSubmit={(next) => {
            setDetails(next);
            setFailure(null);
            setStep("consent");
          }}
          onDifferentNumber={() => {
            setTicket(null);
            setStep("phone");
          }}
        />
      )}

      {step === "consent" && (
        <ConsentStep busy={busy} onAccept={(documents) => void create(documents)} onBack={() => setStep("details")} />
      )}
    </Screen>
  );
}

function DetailsStep({
  initial,
  passwordFailures,
  onSubmit,
  onDifferentNumber,
}: {
  initial: Details;
  passwordFailures: string[];
  onSubmit: (details: Details) => void;
  onDifferentNumber: () => void;
}) {
  const { t } = useI18n();
  const [details, setDetails] = useState(initial);
  const [errors, setErrors] = useState<{ fullName?: string; password?: string }>({});

  function submit() {
    const missing = {
      fullName: details.fullName.trim() ? undefined : t("Signup.nameRequired"),
      password: details.password ? undefined : t("Signup.passwordRequired"),
    };
    setErrors(missing);
    if (!missing.fullName && !missing.password) onSubmit(details);
  }

  return (
    <>
      <Text variant="heading">{t("Signup.detailsTitle")}</Text>
      <Text variant="muted">{t("Signup.detailsHelp")}</Text>
      <TextField
        label={t("Signup.fullNameLabel")}
        value={details.fullName}
        onChangeText={(fullName) => setDetails((d) => ({ ...d, fullName }))}
        error={errors.fullName}
        autoComplete="name"
        textContentType="name"
        autoCapitalize="words"
      />
      <TextField
        label={t("Signup.emailLabel")}
        help={t("Signup.emailHelp")}
        value={details.email}
        onChangeText={(email) => setDetails((d) => ({ ...d, email }))}
        keyboardType="email-address"
        autoCapitalize="none"
        autoComplete="email"
        textContentType="emailAddress"
      />
      <TextField
        label={t("Signup.passwordLabel")}
        value={details.password}
        onChangeText={(password) => setDetails((d) => ({ ...d, password }))}
        error={errors.password}
        password
        autoComplete="new-password"
        textContentType="newPassword"
      />
      <PasswordFailures failures={passwordFailures} />
      <Button label={t("Signup.continue")} onPress={submit} />
      <Button label={t("Signup.differentNumber")} variant="link" onPress={onDifferentNumber} />
    </>
  );
}

function ConsentStep({
  busy,
  onAccept,
  onBack,
}: {
  busy: boolean;
  onAccept: (documents: LegalDocument[]) => void;
  onBack: () => void;
}) {
  const { t, language } = useI18n();
  const palette = usePalette();
  const [documents, setDocuments] = useState<LegalDocument[] | null>(null);
  const [loadFailed, setLoadFailed] = useState(false);
  const [accepted, setAccepted] = useState<Record<string, boolean>>({});
  const [open, setOpen] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let cancelled = false;
    getJson(`/api/legal/current?language=${language}`)
      .then(async (response) => {
        if (!response.ok) throw new Error(String(response.status));
        const loaded = (await response.json()) as LegalDocument[];
        if (!cancelled) setDocuments(loaded);
      })
      .catch(() => {
        if (!cancelled) setLoadFailed(true);
      });
    return () => {
      cancelled = true;
    };
  }, [language, attempt]);

  function submit() {
    if (!documents || documents.some((document) => !accepted[document.contentSha256])) {
      setError(t("Signup.acceptAll"));
      return;
    }
    setError(null);
    onAccept(documents);
  }

  return (
    <>
      <Text variant="heading">{t("Signup.consentTitle")}</Text>
      <Text variant="muted">{t("Signup.consentHelp")}</Text>

      {loadFailed && (
        <>
          <FormError message={t("Signup.documentsUnavailable")} />
          <Button
            label={t("Signup.retry")}
            variant="secondary"
            onPress={() => {
              setLoadFailed(false);
              setAttempt((n) => n + 1);
            }}
          />
        </>
      )}

      {documents?.map((document) => (
        <Card key={document.contentSha256}>
          <Text variant="label">{document.title}</Text>
          <Text variant="muted">{t("Signup.version", { version: document.version })}</Text>
          <Button
            label={t("Signup.readDocument", { title: document.title })}
            variant="link"
            onPress={() => setOpen((current) => (current === document.contentSha256 ? null : document.contentSha256))}
          />
          {open === document.contentSha256 && <Text variant="muted">{document.content}</Text>}
          <View style={styles.accept}>
            <Switch
              accessibilityLabel={t("Signup.accept", { title: document.title })}
              value={accepted[document.contentSha256] ?? false}
              onValueChange={(value) => setAccepted((current) => ({ ...current, [document.contentSha256]: value }))}
              trackColor={{ true: palette.primary, false: palette.hairline }}
            />
            <Text style={styles.acceptLabel}>{t("Signup.accept", { title: document.title })}</Text>
          </View>
        </Card>
      ))}

      {error && <Text variant="error">{error}</Text>}
      <Button label={t("Signup.createAccount")} busy={busy} disabled={!documents} onPress={submit} />
      <Button label={t("Signup.back")} variant="link" disabled={busy} onPress={onBack} />
    </>
  );
}

const styles = StyleSheet.create({
  accept: { flexDirection: "row", alignItems: "center", gap: spacing.sm },
  acceptLabel: { flex: 1 },
});
