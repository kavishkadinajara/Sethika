"use client";

import { useLocale, useTranslations } from "next-intl";
import { useState, type FormEvent, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { OtpInput } from "@/components/ui/otp-input";
import { PasswordInput } from "@/components/ui/password-input";
import { useToast } from "@/components/ui/toast";
import { Link, useRouter } from "@/i18n/navigation";
import { requestResetCode, resetPassword, verifyResetCode, type ApiFailure } from "@/lib/auth/api";
import {
  afterRequestFailure,
  afterResetFailure,
  afterVerifyFailure,
  checkNewPassword,
  resetSteps,
  type MessagePlace,
  type ResetStep,
} from "@/lib/auth/password-reset";
import { cn, focusRing } from "@/lib/utils";
import { AuthHeading } from "./AuthHeading";
import { FormError } from "./FormError";
import { textLink } from "./links";
import { PasswordChecklist, PasswordFailures } from "./PasswordGuidance";
import { StepProgress } from "./StepProgress";
import { formatWait, useCountdown } from "./useCountdown";
import { useApiErrorMessage } from "./useApiErrorMessage";

/** The API's resend cooldown (OtpOptions.ResendCooldown). A 429 with Retry-After overrides it. */
const RESEND_SECONDS = 60;

type FieldErrors = Partial<Record<Exclude<MessagePlace, "form">, ReactNode>>;

/**
 * A forgotten password: phone, then the code (which returns a single-use reset ticket), then the
 * new password twice, then POST /api/auth/password/reset and back to /login. The API answers the
 * phone step the same for every number, so the screen never says whether the number has an
 * account either. The reset starts no session: the user signs in with the new password.
 */
export function ForgotPasswordFlow() {
  const t = useTranslations("ForgotPassword");
  const errorMessage = useApiErrorMessage();
  const locale = useLocale();
  const router = useRouter();
  const toast = useToast();
  const resend = useCountdown();

  const [step, setStep] = useState<ResetStep>("phone");
  const [moved, setMoved] = useState(false);
  const [busy, setBusy] = useState(false);
  const [formError, setFormError] = useState<ReactNode>(null);
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});

  const [phone, setPhone] = useState("");
  const [code, setCode] = useState("");
  const [ticket, setTicket] = useState<string | null>(null);
  const [password, setPassword] = useState("");
  const [confirmation, setConfirmation] = useState("");

  function go(next: ResetStep, error: ReactNode = null) {
    setStep(next);
    setMoved(true);
    setFormError(error);
    setBusy(false);
  }

  /** Shows a failure where the user can act on it. */
  function show(failure: ApiFailure, place: MessagePlace, message: ReactNode = errorMessage(failure)) {
    if (failure.status === 429 && failure.retryAfter) resend.start(failure.retryAfter);
    if (place === "form") setFormError(message);
    else setFieldErrors({ [place]: message });
    setBusy(false);
  }

  // ---- step 1: phone -------------------------------------------------------------------------

  async function sendCode(event?: FormEvent) {
    event?.preventDefault();
    if (busy) return;
    setFormError(null);
    setFieldErrors({});
    if (!phone.trim()) {
      setFieldErrors({ phone: t("phoneRequired") });
      return;
    }
    setBusy(true);
    const result = await requestResetCode(phone.trim(), locale);
    if (!result.ok) {
      show(result.failure, afterRequestFailure(result.failure));
      return;
    }
    resend.start(RESEND_SECONDS);
    setCode("");
    go("code");
  }

  // ---- step 2: code --------------------------------------------------------------------------

  async function checkCode(value = code) {
    if (busy) return;
    setFormError(null);
    setFieldErrors({});
    if (value.length !== 6) {
      setFieldErrors({ code: t("codeRequired") });
      return;
    }
    setBusy(true);
    const result = await verifyResetCode(phone.trim(), value);
    if (!result.ok) {
      setCode("");
      const next = afterVerifyFailure(result.failure);
      setStep(next.step);
      show(result.failure, next.place);
      return;
    }
    setTicket(result.value);
    setPassword("");
    setConfirmation("");
    go("password");
  }

  async function resendCode() {
    if (busy || resend.remaining > 0) return;
    setBusy(true);
    setFieldErrors({});
    const result = await requestResetCode(phone.trim(), locale);
    if (!result.ok) {
      show(result.failure, "form");
      return;
    }
    resend.start(RESEND_SECONDS);
    setCode("");
    setFormError(null);
    setBusy(false);
  }

  // ---- step 3: new password ------------------------------------------------------------------

  async function changePassword(event: FormEvent) {
    event.preventDefault();
    if (busy || !ticket) return;
    setFormError(null);
    const problems = checkNewPassword(password, confirmation);
    setFieldErrors({
      password: problems.password && t("passwordRequired"),
      confirmation:
        problems.confirmation === "mismatch" ? t("confirmMismatch") : problems.confirmation && t("confirmRequired"),
    });
    if (problems.password || problems.confirmation) return;
    setBusy(true);

    const result = await resetPassword(ticket, password);
    if (result.ok) {
      toast({ title: t("changedTitle"), description: t("changedDescription"), variant: "success" });
      router.replace("/login");
      return;
    }

    const { failure } = result;
    const next = afterResetFailure(failure);
    if (!next.keepTicket) setTicket(null);
    if (next.step !== "password") {
      go(next.step, errorMessage(failure));
      return;
    }
    if (next.place === "password") {
      setFormError(errorMessage(failure));
      show(failure, "password", <PasswordFailures failures={failure.passwordFailures} />);
      return;
    }
    show(failure, next.place);
  }

  // ---- render --------------------------------------------------------------------------------

  const stepNumber = resetSteps.indexOf(step) + 1;

  return (
    <div>
      <StepProgress current={stepNumber} total={resetSteps.length} label={t("step", { current: stepNumber, total: resetSteps.length })} />

      {step === "phone" && (
        <form key="phone" noValidate onSubmit={sendCode} className="grid gap-5">
          <AuthHeading title={t("phoneTitle")} description={t("phoneHelp")} focusOnMount={moved} />
          <FormError>{formError}</FormError>
          <Field label={t("phoneLabel")} error={fieldErrors.phone} required>
            <Input
              type="tel"
              inputMode="tel"
              autoComplete="tel"
              placeholder={t("phonePlaceholder")}
              value={phone}
              onChange={(event) => setPhone(event.target.value)}
            />
          </Field>
          <Button type="submit" size="lg" loading={busy} className="w-full">
            {t("sendCode")}
          </Button>
          <p className="flex flex-wrap items-center justify-center gap-x-2 text-center text-muted">
            <span>{t("remembered")}</span>
            <Link href="/login" className={cn(textLink, focusRing)}>
              {t("signIn")}
            </Link>
          </p>
        </form>
      )}

      {step === "code" && (
        <form
          key="code"
          noValidate
          onSubmit={(event) => {
            event.preventDefault();
            void checkCode();
          }}
          className="grid gap-5"
        >
          <AuthHeading title={t("codeTitle")} description={t("codeSentTo", { phone: phone.trim() })} focusOnMount={moved} />
          <FormError>{formError}</FormError>
          <Field label={t("codeLabel")} error={fieldErrors.code} required>
            <OtpInput
              value={code}
              onValueChange={(value) => {
                setCode(value);
                setFieldErrors({});
              }}
              onComplete={(value) => void checkCode(value)}
              disabled={busy}
            />
          </Field>
          <Button type="submit" size="lg" loading={busy} className="w-full">
            {t("verify")}
          </Button>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <Button variant="ghost" onClick={() => go("phone")}>
              {t("changeNumber")}
            </Button>
            <Button variant="ghost" onClick={() => void resendCode()} disabled={resend.remaining > 0}>
              {resend.remaining > 0 ? t("resendIn", { time: formatWait(resend.remaining) }) : t("resend")}
            </Button>
          </div>
        </form>
      )}

      {step === "password" && (
        <form key="password" noValidate onSubmit={changePassword} className="grid gap-5">
          <AuthHeading title={t("passwordTitle")} description={t("passwordHelp")} focusOnMount={moved} />
          <FormError>{formError}</FormError>
          <Field label={t("passwordLabel")} description={<PasswordChecklist password={password} />} error={fieldErrors.password} required>
            <PasswordInput autoComplete="new-password" value={password} onChange={(event) => setPassword(event.target.value)} />
          </Field>
          <Field label={t("confirmLabel")} error={fieldErrors.confirmation} required>
            <PasswordInput autoComplete="new-password" value={confirmation} onChange={(event) => setConfirmation(event.target.value)} />
          </Field>
          <Button type="submit" size="lg" loading={busy} className="w-full">
            {t("submit")}
          </Button>
          <Button
            variant="ghost"
            onClick={() => {
              setTicket(null);
              go("phone");
            }}
          >
            {t("startAgain")}
          </Button>
        </form>
      )}
    </div>
  );
}
