"use client";

import { useTranslations } from "next-intl";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { OtpInput } from "@/components/ui/otp-input";
import { requestStepUpCode, verifyStepUpCode, type ApiFailure } from "@/lib/auth/api";
import { afterStepUpSendFailure } from "@/lib/auth/step-up";
import { FormError } from "./FormError";
import { formatWait, useCountdown } from "./useCountdown";
import { useApiErrorMessage } from "./useApiErrorMessage";

const RESEND_SECONDS = 60;

/**
 * MFA step-up (plan 5.3, A20): texts a code to the account's own phone as soon as it opens,
 * then verifies it. On success the current access token may perform RequiresMfa actions until
 * it is refreshed. Used by the global step-up dialog and by the /stepup page.
 */
export function StepUpForm({ onVerified, onCancel }: { onVerified: () => void; onCancel?: () => void }) {
  const t = useTranslations("StepUp");
  const errorMessage = useApiErrorMessage();
  const resend = useCountdown();
  const sentOnce = useRef(false);

  const [sending, setSending] = useState(true);
  const [busy, setBusy] = useState(false);
  const [code, setCode] = useState("");
  const [formError, setFormError] = useState<string | null>(null);
  const [codeError, setCodeError] = useState<string | null>(null);
  /** no_verified_phone or account_not_active: no code can ever arrive, so only cancel is offered. */
  const [blocked, setBlocked] = useState(false);

  async function send() {
    setSending(true);
    setFormError(null);
    setCodeError(null);
    const result = await requestStepUpCode();
    setSending(false);
    if (result.ok) {
      resend.start(RESEND_SECONDS);
      return;
    }
    report(result.failure);
  }

  function report(failure: ApiFailure) {
    const next = afterStepUpSendFailure(failure);
    if (next.wait) resend.start(next.wait);
    if (next.blocked) setBlocked(true);
    if (next.showError) setFormError(errorMessage(failure));
  }

  useEffect(() => {
    // Strict Mode runs effects twice in development; one SMS per opening is enough.
    if (sentOnce.current) return;
    sentOnce.current = true;
    void send();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- once, on open
  }, []);

  async function verify(value = code, event?: FormEvent) {
    event?.preventDefault();
    if (busy || blocked) return;
    setFormError(null);
    if (value.length !== 6) {
      setCodeError(t("codeRequired"));
      return;
    }
    setBusy(true);
    const result = await verifyStepUpCode(value);
    if (result.ok) {
      onVerified();
      return;
    }
    setBusy(false);
    setCode("");
    if (result.failure.status === 400) setCodeError(errorMessage(result.failure));
    else report(result.failure);
  }

  return (
    <form noValidate onSubmit={(event) => void verify(code, event)} className="grid gap-5">
      <FormError>{formError}</FormError>

      {!blocked && (
        <>
          <p className="text-muted" aria-live="polite">
            {sending ? t("sending") : t("sent")}
          </p>
          <Field label={t("codeLabel")} error={codeError} required>
            <OtpInput
              value={code}
              disabled={busy}
              onValueChange={(value) => {
                setCode(value);
                setCodeError(null);
              }}
              onComplete={(value) => void verify(value)}
            />
          </Field>
          <Button type="submit" size="lg" loading={busy} className="w-full">
            {t("confirm")}
          </Button>
          <Button variant="ghost" onClick={() => void send()} disabled={sending || resend.remaining > 0}>
            {resend.remaining > 0 ? t("resendIn", { time: formatWait(resend.remaining) }) : t("resend")}
          </Button>
        </>
      )}

      {onCancel && (
        <Button variant={blocked ? "primary" : "ghost"} onClick={onCancel} className={blocked ? "w-full" : undefined}>
          {t("cancel")}
        </Button>
      )}
    </form>
  );
}
