"use client";

import type { FormEvent, ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { AuthHeading } from "./AuthHeading";
import { FormError } from "./FormError";

/**
 * The "your mobile number" step that starts every code flow - signup and sign-in by code. Text
 * comes in translated by the flow, so each flow can say what the code is for.
 */
export function PhoneStep({
  text,
  phone,
  onPhoneChange,
  onSubmit,
  busy,
  formError,
  fieldError,
  focusOnMount,
  footer,
}: {
  text: { title: string; description: string; label: string; placeholder: string; submit: string };
  phone: string;
  onPhoneChange: (phone: string) => void;
  onSubmit: (event: FormEvent) => void;
  busy: boolean;
  formError: ReactNode;
  fieldError: ReactNode;
  focusOnMount: boolean;
  /** Links under the button, to the other ways in. */
  footer?: ReactNode;
}) {
  return (
    <form noValidate onSubmit={onSubmit} className="grid gap-5">
      <AuthHeading title={text.title} description={text.description} focusOnMount={focusOnMount} />
      <FormError>{formError}</FormError>
      <Field label={text.label} error={fieldError} required>
        <Input
          type="tel"
          inputMode="tel"
          autoComplete="tel"
          placeholder={text.placeholder}
          value={phone}
          onChange={(event) => onPhoneChange(event.target.value)}
        />
      </Field>
      <Button type="submit" size="lg" loading={busy} className="w-full">
        {text.submit}
      </Button>
      {footer}
    </form>
  );
}
