import { useState } from "react";

import { useI18n } from "@/i18n/i18n-provider";

import { Button } from "./button";
import { TextField } from "./text-field";

/*  The two steps code sign-in and signup share: type a number, then type the code texted to it.
    The screens own the requests and the wording; these own the fields and their checks.      */

export function PhoneStep({
  label,
  help,
  requiredMessage,
  submitLabel,
  busy,
  onSubmit,
  initial = "",
  error: refused,
}: {
  label: string;
  help: string;
  requiredMessage: string;
  submitLabel: string;
  busy: boolean;
  onSubmit: (phone: string) => void;
  initial?: string;
  /** Why the API refused the number. */
  error?: string | null;
}) {
  const { t } = useI18n();
  const [phone, setPhone] = useState(initial);
  const [error, setError] = useState<string | null>(null);

  function submit() {
    if (busy) return; // The keyboard's key does not go through the button.
    if (!phone.trim()) {
      setError(requiredMessage);
      return;
    }
    setError(null);
    onSubmit(phone.trim());
  }

  return (
    <>
      <TextField
        label={label}
        help={help}
        value={phone}
        onChangeText={setPhone}
        error={error ?? refused}
        placeholder={t("Signup.phonePlaceholder")}
        keyboardType="phone-pad"
        autoComplete="tel"
        textContentType="telephoneNumber"
        returnKeyType="send"
        onSubmitEditing={submit}
      />
      <Button label={submitLabel} busy={busy} onPress={submit} />
    </>
  );
}

export function CodeStep({
  submitLabel,
  busy,
  onSubmit,
  onResend,
  onChangeNumber,
  error: refused,
}: {
  submitLabel: string;
  busy: boolean;
  onSubmit: (code: string) => void;
  onResend: () => void;
  onChangeNumber: () => void;
  /** Why the API refused the code. */
  error?: string | null;
}) {
  const { t } = useI18n();
  const [code, setCode] = useState("");
  const [error, setError] = useState<string | null>(null);

  function submit() {
    if (busy) return; // The keyboard's key does not go through the button.
    if (!/^\d{6}$/.test(code)) {
      setError(t("Signup.codeRequired"));
      return;
    }
    setError(null);
    onSubmit(code);
  }

  return (
    <>
      <TextField
        label={t("Signup.codeLabel")}
        value={code}
        onChangeText={(value) => setCode(value.replace(/\D/g, "").slice(0, 6))}
        error={error ?? refused}
        keyboardType="number-pad"
        autoComplete="one-time-code"
        textContentType="oneTimeCode"
        maxLength={6}
        returnKeyType="done"
        onSubmitEditing={submit}
      />
      <Button label={submitLabel} busy={busy} onPress={submit} />
      <Button label={t("Signup.resend")} variant="link" disabled={busy} onPress={onResend} />
      <Button label={t("Signup.changeNumber")} variant="link" disabled={busy} onPress={onChangeNumber} />
    </>
  );
}
