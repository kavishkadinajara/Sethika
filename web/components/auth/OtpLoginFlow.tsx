"use client";

import { useTranslations } from "next-intl";
import { useEffect, useState, type FormEvent, type ReactNode } from "react";
import { Link, useRouter } from "@/i18n/navigation";
import { requestSignInCode, signInWithCode, type ApiFailure } from "@/lib/auth/api";
import { useSession } from "@/lib/auth/useSession";
import { cn, focusRing } from "@/lib/utils";
import { CodeStep } from "./CodeStep";
import { destinationAfterSignIn, useLandAfterSignIn } from "./LoginForm";
import { textLink } from "./links";
import { PhoneStep } from "./PhoneStep";
import { useApiErrorMessage } from "./useApiErrorMessage";
import { useCountdown } from "./useCountdown";

type Step = "phone" | "code";

/** The API's resend cooldown (OtpOptions.ResendCooldown). A 429 with Retry-After overrides it. */
const RESEND_SECONDS = 60;

/**
 * Sign-in by SMS code: phone, then code (POST /api/auth/otp/request with purpose "login", then
 * /api/auth/otp/sign-in). The API answers the request the same whether or not the number has an
 * account, so the code step says a code is coming only "if" the number is on an account - claiming
 * it was sent would be untrue for a mistyped number, and a different message would undo the API's
 * care. A wrong, used or expired code is one error with no attempts count, for the same reason.
 */
export function OtpLoginFlow() {
  const t = useTranslations("LoginOtp");
  const errorMessage = useApiErrorMessage();
  const router = useRouter();
  const land = useLandAfterSignIn();
  const current = useSession();
  const resend = useCountdown();

  const [step, setStep] = useState<Step>("phone");
  const [moved, setMoved] = useState(false);
  const [busy, setBusy] = useState(false);
  const [signedIn, setSignedIn] = useState(false);
  const [formError, setFormError] = useState<ReactNode>(null);
  const [fieldErrors, setFieldErrors] = useState<{ phone?: ReactNode; code?: ReactNode }>({});
  const [phone, setPhone] = useState("");
  const [code, setCode] = useState("");

  // Already signed in (another tab, or the boot refresh): nothing to do here. Not while a code is
  // being checked: that sign-in lands the user itself, in the account's language.
  useEffect(() => {
    if (current.status === "authenticated" && !signedIn && !busy) router.replace(destinationAfterSignIn());
  }, [current.status, signedIn, busy, router]);

  function go(next: Step) {
    setStep(next);
    setMoved(true);
    setFormError(null);
    setFieldErrors({});
    setBusy(false);
  }

  function showFailure(failure: ApiFailure, where: "phone" | "code" | null = null) {
    if (failure.status === 429 && failure.retryAfter) resend.start(failure.retryAfter);
    if (where) setFieldErrors({ [where]: errorMessage(failure) });
    else setFormError(errorMessage(failure));
    setBusy(false);
  }

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
    const result = await requestSignInCode(phone.trim());
    if (!result.ok) {
      showFailure(result.failure, result.failure.code === "invalid_phone_number" ? "phone" : null);
      return;
    }
    resend.start(RESEND_SECONDS);
    setCode("");
    go("code");
  }

  async function resendCode() {
    if (busy || resend.remaining > 0) return;
    setBusy(true);
    setFieldErrors({});
    const result = await requestSignInCode(phone.trim());
    if (!result.ok) {
      showFailure(result.failure);
      return;
    }
    resend.start(RESEND_SECONDS);
    setCode("");
    setFormError(null);
    setBusy(false);
  }

  async function checkCode(value: string) {
    if (busy) return;
    setFormError(null);
    setFieldErrors({});
    if (value.length !== 6) {
      setFieldErrors({ code: t("codeRequired") });
      return;
    }
    setBusy(true);
    const result = await signInWithCode(phone.trim(), value);
    if (result.ok) {
      setSignedIn(true);
      land();
      return;
    }
    setCode("");
    showFailure(result.failure, result.failure.code === "otp_invalid" ? "code" : null);
  }

  const otherWays = (
    <div className="grid justify-items-center gap-1 text-center text-muted">
      <Link href="/login" className={cn(textLink, focusRing)}>
        {t("usePassword")}
      </Link>
      <p className="flex flex-wrap items-center justify-center gap-x-2">
        <span>{t("noAccount")}</span>
        <Link href="/signup" className={cn(textLink, focusRing)}>
          {t("createAccount")}
        </Link>
      </p>
    </div>
  );

  return step === "phone" ? (
    <PhoneStep
      key="phone"
      text={{
        title: t("title"),
        description: t("phoneHelp"),
        label: t("phoneLabel"),
        placeholder: t("phonePlaceholder"),
        submit: t("sendCode"),
      }}
      phone={phone}
      onPhoneChange={setPhone}
      onSubmit={sendCode}
      busy={busy}
      formError={formError}
      fieldError={fieldErrors.phone}
      focusOnMount={moved}
      footer={otherWays}
    />
  ) : (
    <CodeStep
      key="code"
      text={{
        title: t("codeTitle"),
        description: t("codeSentTo", { phone: phone.trim() }),
        label: t("codeLabel"),
        submit: t("signIn"),
        changeNumber: t("changeNumber"),
        resend: t("resend"),
        resendIn: (time) => t("resendIn", { time }),
      }}
      code={code}
      onCodeChange={(value) => {
        setCode(value);
        setFieldErrors({});
      }}
      onCheck={(value) => void checkCode(value)}
      onChangeNumber={() => go("phone")}
      onResend={() => void resendCode()}
      resendRemaining={resend.remaining}
      busy={busy || signedIn}
      formError={formError}
      fieldError={fieldErrors.code}
      focusOnMount={moved}
    />
  );
}
