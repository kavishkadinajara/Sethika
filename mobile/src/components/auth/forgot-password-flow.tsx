import { router } from "expo-router";
import { useState } from "react";

import { describeFailure, networkFailure, postJson } from "@/api/http";
import { passNext } from "@/auth/leave-sign-in";
import {
  afterRequestFailure,
  afterResetFailure,
  afterVerifyFailure,
  checkNewPassword,
  resetSteps,
  type MessagePlace,
  type NewPasswordProblems,
  type ResetFailure,
  type ResetStep,
} from "@/auth/password-reset";
import { Button } from "@/components/button";
import { CodeStep, PhoneStep } from "@/components/code-steps";
import { PasswordFailures } from "@/components/password-failures";
import { BrandHeader, FormError } from "@/components/screen";
import { Text } from "@/components/text";
import { TextField } from "@/components/text-field";
import { useI18n } from "@/i18n/i18n-provider";

type Failure = ResetFailure & { message: string; rules: string[] };
type Answer<T> = { ok: true; value: T } | { ok: false; failure: Failure };
type FieldErrors = Partial<Record<Exclude<MessagePlace, "form">, string>>;

/**
 * A forgotten password (F38): phone, then the code (which returns a single-use reset ticket), then
 * the new password twice, then back to sign-in. The API answers the phone step the same for every
 * number, so nothing here says whether the number has an account either. The ticket lives in this
 * screen's state and dies with it, and the reset starts no session: the donor signs in with the
 * new password.
 */
export function ForgotPasswordFlow({ next }: { next?: string }) {
  const { t, language } = useI18n();
  const [asked, setAsked] = useState<"phone" | "code">("phone");
  const [phone, setPhone] = useState("");
  const [ticket, setTicket] = useState<string | null>(null);
  // Holding a ticket is what the password step is, so losing one always leaves it.
  const step: ResetStep = ticket ? "password" : asked;
  // Bumped to give the code step a fresh, empty field.
  const [attempt, setAttempt] = useState(0);
  const [busy, setBusy] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
  const [rules, setRules] = useState<string[]>([]);

  /** Sends a request with the old messages cleared. A lost connection is a failure like any other. */
  async function ask<T>(request: () => Promise<Response>, read: (response: Response) => Promise<T>): Promise<Answer<T>> {
    setBusy(true);
    setFormError(null);
    setFieldErrors({});
    setRules([]);
    try {
      const response = await request();
      if (response.ok) return { ok: true, value: await read(response) };
      const refused = await describeFailure(response, t);
      const failures = refused.body?.failures;
      return {
        ok: false,
        failure: {
          status: response.status,
          code: refused.code,
          message: refused.message,
          rules: Array.isArray(failures) ? (failures as string[]) : [],
        },
      };
    } catch {
      return { ok: false, failure: { status: 0, code: null, message: networkFailure(t).message, rules: [] } };
    } finally {
      setBusy(false);
    }
  }

  function show(failure: Failure, place: MessagePlace) {
    if (place === "form") setFormError(failure.message);
    else setFieldErrors({ [place]: failure.message });
  }

  const sendCode = (number: string) =>
    ask(
      // The answer is the same whether or not the number has an account (PasswordResetHandler).
      () => postJson("/api/auth/otp/request", { phoneNumber: number, purpose: "reset", language }),
      async () => undefined,
    );

  async function requestCode(number: string) {
    const answer = await sendCode(number);
    if (!answer.ok) {
      show(answer.failure, afterRequestFailure(answer.failure));
      return;
    }
    setPhone(number);
    setAttempt((value) => value + 1);
    setAsked("code");
  }

  async function resendCode() {
    const answer = await sendCode(phone);
    if (!answer.ok) show(answer.failure, "form");
    else setAttempt((value) => value + 1);
  }

  async function checkCode(code: string) {
    const answer = await ask(
      () => postJson("/api/auth/otp/verify", { phoneNumber: phone, purpose: "reset", code }),
      async (response) => ((await response.json()) as { ticket: string }).ticket,
    );
    if (!answer.ok) {
      setAttempt((value) => value + 1);
      show(answer.failure, afterVerifyFailure(answer.failure).place);
      return;
    }
    setTicket(answer.value);
  }

  async function changePassword(password: string) {
    const answer = await ask(
      () => postJson("/api/auth/password/reset", { ticket, newPassword: password }),
      async () => undefined,
    );
    if (answer.ok) {
      router.dismissTo({ pathname: "/sign-in", params: { reset: "1", ...passNext(next) } });
      return;
    }

    const { failure } = answer;
    const after = afterResetFailure(failure);
    if (!after.keepTicket) {
      setTicket(null);
      setAsked("phone");
    }
    // The summary goes above the form, and the broken rules under the field that can fix them.
    setFormError(failure.message);
    if (after.place === "password") setRules(failure.rules);
  }

  function startAgain() {
    setTicket(null);
    setFormError(null);
    setFieldErrors({});
    setRules([]);
    setAsked("phone");
  }

  return (
    <>
      <BrandHeader />
      <Text variant="muted">
        {t("ForgotPassword.step", { current: resetSteps.indexOf(step) + 1, total: resetSteps.length })}
      </Text>

      {step === "phone" && (
        <>
          <Text variant="title" accessibilityRole="header">
            {t("ForgotPassword.phoneTitle")}
          </Text>
          <FormError message={formError} />
          <PhoneStep
            label={t("ForgotPassword.phoneLabel")}
            help={t("ForgotPassword.phoneHelp")}
            requiredMessage={t("ForgotPassword.phoneRequired")}
            submitLabel={t("ForgotPassword.sendCode")}
            busy={busy}
            error={fieldErrors.phone}
            initial={phone}
            onSubmit={(number) => void requestCode(number)}
          />
          <Text variant="muted">{t("ForgotPassword.remembered")}</Text>
          <Button label={t("ForgotPassword.signIn")} variant="link" onPress={() => router.back()} />
        </>
      )}

      {step === "code" && (
        <>
          <Text variant="title" accessibilityRole="header">
            {t("ForgotPassword.codeTitle")}
          </Text>
          <Text variant="muted">{t("ForgotPassword.codeSentTo", { phone })}</Text>
          <FormError message={formError} />
          <CodeStep
            key={attempt}
            submitLabel={t("ForgotPassword.verify")}
            busy={busy}
            error={fieldErrors.code}
            onSubmit={(code) => void checkCode(code)}
            onResend={() => void resendCode()}
            onChangeNumber={startAgain}
          />
        </>
      )}

      {step === "password" && (
        <>
          <Text variant="title" accessibilityRole="header">
            {t("ForgotPassword.passwordTitle")}
          </Text>
          <Text variant="muted">{t("ForgotPassword.passwordHelp")}</Text>
          <FormError message={formError} />
          <NewPasswordStep
            busy={busy}
            rules={rules}
            onSubmit={(password) => void changePassword(password)}
            onStartAgain={startAgain}
          />
        </>
      )}
    </>
  );
}

function NewPasswordStep({
  busy,
  rules,
  onSubmit,
  onStartAgain,
}: {
  busy: boolean;
  rules: string[];
  onSubmit: (password: string) => void;
  onStartAgain: () => void;
}) {
  const { t } = useI18n();
  const [password, setPassword] = useState("");
  const [confirmation, setConfirmation] = useState("");
  const [problems, setProblems] = useState<NewPasswordProblems>({});

  function submit() {
    // The keyboard's key does not go through the button, and a ticket can only be spent once.
    if (busy) return;
    const found = checkNewPassword(password, confirmation);
    setProblems(found);
    if (!found.password && !found.confirmation) onSubmit(password);
  }

  return (
    <>
      <TextField
        label={t("ForgotPassword.passwordLabel")}
        value={password}
        onChangeText={setPassword}
        error={problems.password && t("ForgotPassword.passwordRequired")}
        password
        autoComplete="new-password"
        textContentType="newPassword"
        returnKeyType="next"
      />
      <PasswordFailures failures={rules} />
      <TextField
        label={t("ForgotPassword.confirmLabel")}
        value={confirmation}
        onChangeText={setConfirmation}
        error={
          problems.confirmation === "mismatch"
            ? t("ForgotPassword.confirmMismatch")
            : problems.confirmation && t("ForgotPassword.confirmRequired")
        }
        password
        autoComplete="new-password"
        textContentType="newPassword"
        returnKeyType="go"
        onSubmitEditing={submit}
      />
      <Button label={t("ForgotPassword.submit")} busy={busy} onPress={submit} />
      <Button label={t("ForgotPassword.startAgain")} variant="link" disabled={busy} onPress={onStartAgain} />
    </>
  );
}
