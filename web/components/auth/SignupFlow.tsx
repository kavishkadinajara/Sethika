"use client";

import { useLocale, useTranslations } from "next-intl";
import { useCallback, useEffect, useState, type FormEvent, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { PasswordInput } from "@/components/ui/password-input";
import { Spinner } from "@/components/ui/spinner";
import { Link, useRouter } from "@/i18n/navigation";
import {
  getLegalDocuments,
  requestSignupCode,
  signUp,
  verifySignupCode,
  type ApiFailure,
  type LegalDocument,
} from "@/lib/auth/api";
import { readNextPath } from "@/lib/auth/next-path";
import { cn, focusRing } from "@/lib/utils";
import { AuthHeading } from "./AuthHeading";
import { CodeStep } from "./CodeStep";
import { FormError } from "./FormError";
import { PasswordChecklist, PasswordFailures } from "./PasswordGuidance";
import { textLink } from "./links";
import { PhoneStep } from "./PhoneStep";
import { StepProgress } from "./StepProgress";
import { useCountdown } from "./useCountdown";
import { useApiErrorMessage } from "./useApiErrorMessage";

type Step = "phone" | "code" | "details" | "consent" | "created";
const numbered: Step[] = ["phone", "code", "details", "consent"];

/** The API's resend cooldown (OtpOptions.ResendCooldown). A 429 with Retry-After overrides it. */
const RESEND_SECONDS = 60;

type FieldErrors = Partial<Record<"phone" | "code" | "fullName" | "email" | "password", ReactNode>>;

/**
 * Donor signup: phone, then the code (which returns a single-use ticket, A17), then name and
 * password, then the legal documents, then POST /api/auth/signup. The API checks the form
 * before it spends the ticket, so a rejected password or a missing consent is fixed and
 * resubmitted with the same ticket; only phone_not_verified sends the donor back to step one.
 */
export function SignupFlow() {
  const t = useTranslations("Signup");
  const errorMessage = useApiErrorMessage();
  const locale = useLocale();
  const router = useRouter();
  const resend = useCountdown();

  const [step, setStep] = useState<Step>("phone");
  const [moved, setMoved] = useState(false);
  const [busy, setBusy] = useState(false);
  const [formError, setFormError] = useState<ReactNode>(null);
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});

  const [phone, setPhone] = useState("");
  const [code, setCode] = useState("");
  const [ticket, setTicket] = useState<string | null>(null);
  const [fullName, setFullName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [documents, setDocuments] = useState<LegalDocument[] | null>(null);
  const [documentsFailed, setDocumentsFailed] = useState(false);
  const [accepted, setAccepted] = useState<Set<string>>(new Set());

  function go(next: Step, error: ReactNode = null) {
    setStep(next);
    setMoved(true);
    setFormError(error);
    setBusy(false);
  }

  /** Shows a failure where the donor can act on it. */
  function showFailure(failure: ApiFailure, where: keyof FieldErrors | null = null) {
    if (failure.status === 429 && failure.retryAfter) resend.start(failure.retryAfter);
    if (where) setFieldErrors({ [where]: errorMessage(failure) });
    else setFormError(errorMessage(failure));
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
    const result = await requestSignupCode(phone.trim(), locale);
    if (!result.ok) {
      showFailure(result.failure, result.failure.code === "invalid_phone_number" ? "phone" : null);
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
    const result = await verifySignupCode(phone.trim(), value);
    if (!result.ok) {
      setCode("");
      showFailure(result.failure, result.failure.status === 400 ? "code" : null);
      return;
    }
    setTicket(result.value);
    go("details");
  }

  async function resendCode() {
    if (busy || resend.remaining > 0) return;
    setBusy(true);
    setFieldErrors({});
    const result = await requestSignupCode(phone.trim(), locale);
    if (!result.ok) {
      showFailure(result.failure);
      return;
    }
    resend.start(RESEND_SECONDS);
    setCode("");
    setFormError(null);
    setBusy(false);
  }

  // ---- step 3: details -----------------------------------------------------------------------

  function checkDetails(event: FormEvent) {
    event.preventDefault();
    const errors: FieldErrors = {};
    if (!fullName.trim()) errors.fullName = t("nameRequired");
    if (!password) errors.password = t("passwordRequired");
    setFieldErrors(errors);
    if (Object.keys(errors).length === 0) go("consent");
  }

  // ---- step 4: consent and submit ------------------------------------------------------------

  const loadDocuments = useCallback(async () => {
    setDocumentsFailed(false);
    const result = await getLegalDocuments(locale);
    if (result.ok) {
      setDocuments(result.value);
      // A document that changed since it was ticked must be read and ticked again.
      setAccepted((previous) => new Set(result.value.map((d) => d.contentSha256).filter((h) => previous.has(h))));
    } else {
      setDocumentsFailed(true);
    }
  }, [locale]);

  useEffect(() => {
    // Loading the documents is the point of this step; the state it sets is the fetched data.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    if (step === "consent" && documents === null) void loadDocuments();
  }, [step, documents, loadDocuments]);

  async function createAccount(event: FormEvent) {
    event.preventDefault();
    if (busy || !documents || !ticket) return;
    setFormError(null);
    if (documents.some((d) => !accepted.has(d.contentSha256))) {
      setFormError(t("acceptAll"));
      return;
    }
    setBusy(true);

    const result = await signUp({
      phoneNumber: phone.trim(),
      verificationTicket: ticket,
      fullName: fullName.trim(),
      email: email.trim() || null,
      password,
      preferredLanguage: locale,
      acceptedDocumentHashes: [...accepted],
    });

    if (result.ok) {
      if (result.value.signedIn) router.replace(readNextPath());
      else go("created");
      return;
    }

    const { failure } = result;
    switch (failure.code) {
      case "password_rejected":
        setFieldErrors({ password: <PasswordFailures failures={failure.passwordFailures} /> });
        go("details", errorMessage(failure));
        return;
      case "invalid_full_name":
        setFieldErrors({ fullName: errorMessage(failure) });
        go("details");
        return;
      case "invalid_email":
        setFieldErrors({ email: errorMessage(failure) });
        go("details");
        return;
      case "phone_not_verified":
      case "invalid_phone_number":
        // The ticket is spent or expired: verify the number again.
        setTicket(null);
        go("phone", errorMessage(failure));
        return;
      case "consent_missing":
        setDocuments(null); // the documents may have changed: load them again
        setFormError(errorMessage(failure));
        setBusy(false);
        return;
      case "already_registered":
        setFormError(
          <>
            {errorMessage(failure)}{" "}
            <Link href="/login" className={cn(textLink, focusRing, "min-h-0")}>
              {t("signInInstead")}
            </Link>
          </>,
        );
        setBusy(false);
        return;
      default:
        showFailure(failure);
    }
  }

  // ---- render --------------------------------------------------------------------------------

  const stepNumber = numbered.indexOf(step) + 1;

  return (
    <div>
      {stepNumber > 0 && <StepProgress current={stepNumber} total={numbered.length} label={t("step", { current: stepNumber, total: numbered.length })} />}

      {step === "phone" && (
        <PhoneStep
          key="phone"
          text={{
            title: t("phoneTitle"),
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
          footer={
            <p className="flex flex-wrap items-center justify-center gap-x-2 text-center text-muted">
              <span>{t("haveAccount")}</span>
              <Link href="/login" className={cn(textLink, focusRing)}>
                {t("signIn")}
              </Link>
            </p>
          }
        />
      )}

      {step === "code" && (
        <CodeStep
          key="code"
          text={{
            title: t("codeTitle"),
            description: t("codeSentTo", { phone: phone.trim() }),
            label: t("codeLabel"),
            submit: t("verify"),
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
          busy={busy}
          formError={formError}
          fieldError={fieldErrors.code}
          focusOnMount={moved}
        />
      )}

      {step === "details" && (
        <form key="details" noValidate onSubmit={checkDetails} className="grid gap-5">
          <AuthHeading title={t("detailsTitle")} description={t("detailsHelp")} focusOnMount={moved} />
          <FormError>{formError}</FormError>
          <Field label={t("fullNameLabel")} error={fieldErrors.fullName} required>
            <Input autoComplete="name" value={fullName} onChange={(event) => setFullName(event.target.value)} />
          </Field>
          <Field label={t("emailLabel")} description={t("emailHelp")} error={fieldErrors.email}>
            <Input
              type="email"
              inputMode="email"
              autoComplete="email"
              autoCapitalize="none"
              spellCheck={false}
              value={email}
              onChange={(event) => setEmail(event.target.value)}
            />
          </Field>
          <Field label={t("passwordLabel")} description={<PasswordChecklist password={password} />} error={fieldErrors.password} required>
            <PasswordInput autoComplete="new-password" value={password} onChange={(event) => setPassword(event.target.value)} />
          </Field>
          <Button type="submit" size="lg" className="w-full">
            {t("continue")}
          </Button>
          <Button variant="ghost" onClick={() => { setTicket(null); go("phone"); }}>
            {t("differentNumber")}
          </Button>
        </form>
      )}

      {step === "consent" && (
        <form key="consent" noValidate onSubmit={createAccount} className="grid gap-5">
          <AuthHeading title={t("consentTitle")} description={t("consentHelp")} focusOnMount={moved} />
          <FormError>{formError}</FormError>

          {documents === null && !documentsFailed && (
            <div className="flex justify-center py-6 text-brand">
              <Spinner />
            </div>
          )}
          {documentsFailed && (
            <FormError>
              {t("documentsUnavailable")}{" "}
              <button type="button" onClick={() => void loadDocuments()} className={cn(textLink, focusRing, "min-h-0")}>
                {t("retry")}
              </button>
            </FormError>
          )}

          {documents?.map((document) => (
            <fieldset key={document.contentSha256} className="grid gap-2 rounded-cup border border-hairline bg-surface p-4">
              <legend className="sr-only">{document.title}</legend>
              <details className="group">
                <summary className={cn("flex min-h-11 cursor-pointer items-center justify-between gap-3 rounded-md font-medium text-brand", focusRing)}>
                  <span>{t("readDocument", { title: document.title })}</span>
                  <span className="text-sm font-normal text-muted">{t("version", { version: document.version })}</span>
                </summary>
                <div className="mt-2 max-h-64 overflow-y-auto overscroll-contain whitespace-pre-line rounded-md bg-subtle p-3 text-sm leading-relaxed">
                  {document.content}
                </div>
              </details>
              <label className="flex min-h-11 cursor-pointer items-start gap-3 pt-1">
                <input
                  type="checkbox"
                  checked={accepted.has(document.contentSha256)}
                  onChange={(event) =>
                    setAccepted((previous) => {
                      const next = new Set(previous);
                      if (event.target.checked) next.add(document.contentSha256);
                      else next.delete(document.contentSha256);
                      return next;
                    })
                  }
                  className="mt-0.5 size-5 shrink-0 accent-[var(--color-primary)]"
                />
                <span>{t("accept", { title: document.title })}</span>
              </label>
            </fieldset>
          ))}

          <Button type="submit" size="lg" loading={busy} disabled={!documents} className="w-full">
            {t("createAccount")}
          </Button>
          <Button variant="ghost" onClick={() => go("details")}>
            {t("back")}
          </Button>
        </form>
      )}

      {step === "created" && (
        <div className="grid gap-5">
          <AuthHeading title={t("createdTitle")} description={t("createdSignIn")} focusOnMount />
          <Button asChild size="lg" className="w-full">
            <Link href="/login">{t("signIn")}</Link>
          </Button>
        </div>
      )}
    </div>
  );
}
