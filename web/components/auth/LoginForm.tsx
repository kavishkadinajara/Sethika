"use client";

import { useTranslations } from "next-intl";
import { useEffect, useState, type FormEvent } from "react";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { PasswordInput } from "@/components/ui/password-input";
import { Link, useRouter } from "@/i18n/navigation";
import { cn, focusRing } from "@/lib/utils";
import { logIn, type ApiFailure } from "@/lib/auth/api";
import { readNextPath } from "@/lib/auth/next-path";
import { session } from "@/lib/auth";
import { useSession } from "@/lib/auth/useSession";
import { AuthHeading } from "./AuthHeading";
import { FormError } from "./FormError";
import { textLink } from "./links";
import { useApiErrorMessage } from "./useApiErrorMessage";

/** Where a fresh session should land: the institution picker first if there is a choice to make. */
export function destinationAfterSignIn() {
  const next = readNextPath();
  const state = session.getState();
  if (state.status === "authenticated" && state.institutions.length > 1) {
    return `/select-institution?next=${encodeURIComponent(next)}`;
  }
  return next;
}

/** Phone number or email, and a password (POST /api/auth/login). */
export function LoginForm() {
  const t = useTranslations("Login");
  const errorMessage = useApiErrorMessage();
  const router = useRouter();
  const current = useSession();
  const [login, setLogin] = useState("");
  const [password, setPassword] = useState("");
  const [failure, setFailure] = useState<ApiFailure | null>(null);
  const [missing, setMissing] = useState({ login: false, password: false });
  const [busy, setBusy] = useState(false);

  // Already signed in (another tab, or the boot refresh): nothing to do here.
  useEffect(() => {
    if (current.status === "authenticated" && !busy) router.replace(destinationAfterSignIn());
  }, [current.status, busy, router]);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    setFailure(null);
    const empty = { login: !login.trim(), password: !password };
    setMissing(empty);
    if (empty.login || empty.password) return;
    setBusy(true);

    const result = await logIn(login.trim(), password);
    if (result.ok) {
      router.replace(destinationAfterSignIn());
      return;
    }

    setFailure(result.failure);
    // A wrong password should be retyped, not edited blind.
    if (result.failure.code === "invalid_credentials") setPassword("");
    setBusy(false);
  }

  return (
    <>
      <AuthHeading title={t("title")} description={t("subtitle")} />

      <form noValidate onSubmit={submit} className="grid gap-5">
        <FormError>{failure && errorMessage(failure)}</FormError>

        <Field
          label={t("loginLabel")}
          description={t("loginHelp")}
          error={missing.login ? t("loginRequired") : null}
          required
        >
          <Input
            name="username"
            autoComplete="username"
            inputMode="email"
            autoCapitalize="none"
            spellCheck={false}
            value={login}
            onChange={(event) => setLogin(event.target.value)}
          />
        </Field>

        <Field label={t("passwordLabel")} error={missing.password ? t("passwordRequired") : null} required>
          <PasswordInput
            name="password"
            autoComplete="current-password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
          />
        </Field>

        <Link href="/forgot-password" className={cn(textLink, focusRing, "justify-self-end")}>
          {t("forgotPassword")}
        </Link>

        <Button type="submit" size="lg" loading={busy} className="w-full">
          {t("submit")}
        </Button>
      </form>

      <p className="mt-4 text-center">
        <Link href="/login/otp" className={cn(textLink, focusRing)}>
          {t("useCode")}
        </Link>
      </p>

      <p className="mt-4 flex flex-wrap items-center justify-center gap-x-2 text-center text-muted">
        <span>{t("noAccount")}</span>
        <Link href="/signup" className={cn(textLink, focusRing)}>
          {t("createAccount")}
        </Link>
      </p>
    </>
  );
}
