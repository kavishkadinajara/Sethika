"use client";

import type { ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { OtpInput } from "@/components/ui/otp-input";
import { AuthHeading } from "./AuthHeading";
import { FormError } from "./FormError";
import { formatWait } from "./useCountdown";

/**
 * The six-digit code step every code flow shares: the code boxes, submit on the sixth digit, and
 * the change-number and resend buttons with the resend countdown.
 */
export function CodeStep({
  text,
  code,
  onCodeChange,
  onCheck,
  onChangeNumber,
  onResend,
  resendRemaining,
  busy,
  formError,
  fieldError,
  focusOnMount,
}: {
  text: {
    title: string;
    description: string;
    label: string;
    submit: string;
    changeNumber: string;
    resend: string;
    /** "New code in {time}", given the formatted wait. */
    resendIn: (time: string) => string;
  };
  code: string;
  onCodeChange: (code: string) => void;
  /** Called with the code on submit, and as soon as the sixth digit is typed or pasted. */
  onCheck: (code: string) => void;
  onChangeNumber: () => void;
  onResend: () => void;
  resendRemaining: number;
  busy: boolean;
  formError: ReactNode;
  fieldError: ReactNode;
  focusOnMount: boolean;
}) {
  return (
    <form
      noValidate
      onSubmit={(event) => {
        event.preventDefault();
        onCheck(code);
      }}
      className="grid gap-5"
    >
      <AuthHeading title={text.title} description={text.description} focusOnMount={focusOnMount} />
      <FormError>{formError}</FormError>
      <Field label={text.label} error={fieldError} required>
        <OtpInput value={code} onValueChange={onCodeChange} onComplete={onCheck} disabled={busy} />
      </Field>
      <Button type="submit" size="lg" loading={busy} className="w-full">
        {text.submit}
      </Button>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <Button variant="ghost" onClick={onChangeNumber}>
          {text.changeNumber}
        </Button>
        <Button variant="ghost" onClick={onResend} disabled={resendRemaining > 0}>
          {resendRemaining > 0 ? text.resendIn(formatWait(resendRemaining)) : text.resend}
        </Button>
      </div>
    </form>
  );
}
