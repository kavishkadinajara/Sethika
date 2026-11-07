import { router } from "expo-router";
import { useState } from "react";

import { describeFailure, networkFailure } from "@/api/http";
import {
  applyPasswordChangeFailure,
  checkPasswordChange,
  emptyPasswordChange,
  type PasswordChangeErrors,
  type PasswordChangeField,
  type PasswordChangeProblem,
  type PasswordChangeValues,
} from "@/auth/password-change";
import { useSession, useSessionClient } from "@/auth/session-provider";
import { Button } from "@/components/button";
import { PasswordFailures } from "@/components/password-failures";
import { FormError } from "@/components/screen";
import { Text } from "@/components/text";
import { TextField } from "@/components/text-field";
import { useI18n } from "@/i18n/i18n-provider";

/**
 * Current, new and confirm (F39). Success keeps this phone signed in, on the new session the
 * answer carries, and signs every other device out.
 */
export function ChangePasswordForm() {
  const { t } = useI18n();
  const client = useSessionClient();
  const session = useSession();
  const [values, setValues] = useState<PasswordChangeValues>(emptyPasswordChange);
  const [errors, setErrors] = useState<PasswordChangeErrors>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [changed, setChanged] = useState(false);

  function set(field: PasswordChangeField, value: string) {
    setValues((previous) => ({ ...previous, [field]: value }));
  }

  function message(field: PasswordChangeField, problem: PasswordChangeProblem | undefined): string | undefined {
    switch (problem?.kind) {
      case undefined:
        return undefined;
      case "required":
        return t(`PasswordChange.${field}Required`);
      case "incorrect":
        return t("PasswordChange.currentIncorrect");
      case "unchanged":
        return t("PasswordChange.unchanged");
      case "mismatch":
        return t("PasswordChange.confirmMismatch");
      case "rejected":
        return t("Errors.password_rejected");
    }
  }

  async function submit() {
    if (busy) return;
    setFormError(null);

    const problems = checkPasswordChange(values);
    setErrors(problems);
    if (Object.keys(problems).length > 0) return;

    setBusy(true);
    const result = await client.changePassword(values.current, values.next);

    if (result.ok) {
      setChanged(true);
    } else if (!result.response) {
      setFormError(networkFailure(t).message);
    } else {
      const refused = await describeFailure(result.response, t);
      const failures = refused.body?.failures;
      const outcome = applyPasswordChangeFailure(values, {
        code: refused.code,
        rules: Array.isArray(failures) ? (failures as string[]) : [],
      });
      setValues(outcome.values);
      setErrors(outcome.errors);
      setFormError(outcome.formFailure ? refused.message : null);
    }
    setBusy(false);
  }

  if (changed) {
    return (
      <>
        <Text variant="title" accessibilityRole="header" accessibilityLiveRegion="polite">
          {t("PasswordChange.changed")}
        </Text>
        <Text variant="muted">{t("PasswordChange.changedDescription")}</Text>
        <Button label={t("PasswordChange.home")} variant="secondary" onPress={() => router.dismissTo("/")} />
      </>
    );
  }

  if (session.status !== "authenticated") {
    return (
      <>
        <Text variant="title" accessibilityRole="header">
          {t("PasswordChange.title")}
        </Text>
        <Text variant="muted">{t("PasswordChange.signInFirst")}</Text>
        <Button label={t("PasswordChange.signIn")} onPress={() => router.push("/sign-in")} />
      </>
    );
  }

  return (
    <>
      <Text variant="title" accessibilityRole="header">
        {t("PasswordChange.title")}
      </Text>
      <Text variant="muted">{t("PasswordChange.description")}</Text>

      <FormError message={formError} />

      <TextField
        label={t("PasswordChange.currentLabel")}
        value={values.current}
        onChangeText={(value) => set("current", value)}
        error={message("current", errors.current)}
        password
        autoComplete="current-password"
        textContentType="password"
        returnKeyType="next"
      />
      <TextField
        label={t("PasswordChange.nextLabel")}
        value={values.next}
        onChangeText={(value) => set("next", value)}
        error={message("next", errors.next)}
        password
        autoComplete="new-password"
        textContentType="newPassword"
        returnKeyType="next"
      />
      {errors.next?.kind === "rejected" && <PasswordFailures failures={errors.next.rules} />}
      <TextField
        label={t("PasswordChange.confirmLabel")}
        value={values.confirm}
        onChangeText={(value) => set("confirm", value)}
        error={message("confirm", errors.confirm)}
        password
        autoComplete="new-password"
        textContentType="newPassword"
        returnKeyType="go"
        onSubmitEditing={() => void submit()}
      />

      <Button label={t("PasswordChange.submit")} busy={busy} onPress={() => void submit()} />
    </>
  );
}
