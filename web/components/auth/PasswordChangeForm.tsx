"use client";

import { useTranslations } from "next-intl";
import { useState, type FormEvent, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { PasswordInput } from "@/components/ui/password-input";
import { Spinner } from "@/components/ui/spinner";
import { useToast } from "@/components/ui/toast";
import { Link } from "@/i18n/navigation";
import { changePassword, type ApiFailure } from "@/lib/auth/api";
import {
  applyPasswordChangeFailure,
  checkPasswordChange,
  emptyPasswordChange,
  type PasswordChangeErrors,
  type PasswordChangeField,
  type PasswordChangeProblem,
  type PasswordChangeValues,
} from "@/lib/auth/password-change";
import { useSession } from "@/lib/auth/useSession";
import { cn, focusRing } from "@/lib/utils";
import { AuthHeading } from "./AuthHeading";
import { FormError } from "./FormError";
import { textLink } from "./links";
import { PasswordChecklist, PasswordFailures } from "./PasswordGuidance";
import { useApiErrorMessage } from "./useApiErrorMessage";

/**
 * /account/password: current, new and confirm (POST /api/me/password). Success keeps this
 * device signed in - the API answers with a fresh session - and signs every other device out.
 */
export function PasswordChangeForm() {
  const t = useTranslations("PasswordChange");
  const errorMessage = useApiErrorMessage();
  const toast = useToast();
  const state = useSession();
  const [values, setValues] = useState<PasswordChangeValues>(emptyPasswordChange);
  const [errors, setErrors] = useState<PasswordChangeErrors>({});
  const [formFailure, setFormFailure] = useState<ApiFailure | null>(null);
  const [busy, setBusy] = useState(false);

  if (state.status === "loading") {
    return (
      <div className="flex justify-center py-10 text-brand">
        <Spinner />
      </div>
    );
  }

  if (state.status !== "authenticated") {
    return (
      <>
        <AuthHeading title={t("title")} description={t("signInFirst")} />
        <Link href={`/login?next=${encodeURIComponent("/account/password")}`} className={cn(textLink, focusRing)}>
          {t("signIn")}
        </Link>
      </>
    );
  }

  function set(field: PasswordChangeField, value: string) {
    setValues((previous) => ({ ...previous, [field]: value }));
  }

  function message(field: PasswordChangeField, problem: PasswordChangeProblem | undefined): ReactNode {
    switch (problem?.kind) {
      case undefined:
        return null;
      case "required":
        return t(`${field}Required`);
      case "incorrect":
        return t("currentIncorrect");
      case "unchanged":
        return t("unchanged");
      case "mismatch":
        return t("confirmMismatch");
      case "rejected":
        return <PasswordFailures failures={problem.rules} />;
    }
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    setFormFailure(null);

    const problems = checkPasswordChange(values);
    setErrors(problems);
    if (Object.keys(problems).length > 0) return;

    setBusy(true);
    const result = await changePassword(values.current, values.next);
    setBusy(false);

    if (result.ok) {
      setValues(emptyPasswordChange);
      toast({ title: t("changed"), description: t("changedDescription"), variant: "success" });
      return;
    }

    const outcome = applyPasswordChangeFailure(values, result.failure);
    setValues(outcome.values);
    setErrors(outcome.errors);
    setFormFailure(outcome.formFailure);
  }

  return (
    <>
      <AuthHeading title={t("title")} description={t("description")} />

      <form noValidate onSubmit={submit} className="grid gap-5">
        <FormError>{formFailure && errorMessage(formFailure)}</FormError>

        <Field label={t("currentLabel")} error={message("current", errors.current)} required>
          <PasswordInput
            name="current-password"
            autoComplete="current-password"
            value={values.current}
            onChange={(event) => set("current", event.target.value)}
          />
        </Field>

        <Field
          label={t("nextLabel")}
          description={<PasswordChecklist password={values.next} />}
          error={message("next", errors.next)}
          required
        >
          <PasswordInput
            name="new-password"
            autoComplete="new-password"
            value={values.next}
            onChange={(event) => set("next", event.target.value)}
          />
        </Field>

        <Field label={t("confirmLabel")} error={message("confirm", errors.confirm)} required>
          <PasswordInput
            name="confirm-password"
            autoComplete="new-password"
            value={values.confirm}
            onChange={(event) => set("confirm", event.target.value)}
          />
        </Field>

        <Button type="submit" size="lg" loading={busy} className="w-full">
          {t("submit")}
        </Button>
      </form>

      <p className="mt-8 text-center">
        <Link href="/" className={cn(textLink, focusRing)}>
          {t("home")}
        </Link>
      </p>
    </>
  );
}
