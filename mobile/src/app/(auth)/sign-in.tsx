import { router, useLocalSearchParams } from "expo-router";
import { useState } from "react";

import { describeFailure, networkFailure, postJson } from "@/api/http";
import { leaveSignIn, passNext } from "@/auth/leave-sign-in";
import { useSessionClient } from "@/auth/session-provider";
import { Button } from "@/components/button";
import { BrandHeader, Card, FormError, Screen } from "@/components/screen";
import { Text } from "@/components/text";
import { TextField } from "@/components/text-field";
import { useI18n } from "@/i18n/i18n-provider";

export default function SignInScreen() {
  const { t } = useI18n();
  const client = useSessionClient();
  // Set by the booking sheet: the institution page to come back to (auth/leave-sign-in.ts).
  // `reset`: the forgotten-password flow has just finished and sent the donor back here.
  const { next, reset } = useLocalSearchParams<{ next?: string; reset?: string }>();
  const [login, setLogin] = useState("");
  const [password, setPassword] = useState("");
  const [errors, setErrors] = useState<{ login?: string; password?: string }>({});
  const [failure, setFailure] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit() {
    const missing = {
      login: login.trim() ? undefined : t("Login.loginRequired"),
      password: password ? undefined : t("Login.passwordRequired"),
    };
    setErrors(missing);
    if (missing.login || missing.password) return;

    setBusy(true);
    setFailure(null);
    try {
      const response = await postJson("/api/auth/mobile/login", { login: login.trim(), password });
      if (response.ok) {
        await client.establish(response);
        leaveSignIn(next);
        return;
      }
      setFailure((await describeFailure(response, t)).message);
    } catch {
      setFailure(networkFailure(t).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <Screen>
      <BrandHeader />
      <Text variant="title" accessibilityRole="header">
        {t("Login.title")}
      </Text>
      <Text variant="muted">{t("Login.subtitle")}</Text>

      {reset === "1" && (
        <Card>
          <Text variant="label" accessibilityLiveRegion="polite">
            {t("ForgotPassword.changedTitle")}
          </Text>
          <Text variant="muted">{t("ForgotPassword.changedDescription")}</Text>
        </Card>
      )}

      <FormError message={failure} />

      <TextField
        label={t("Login.loginLabel")}
        help={t("Login.loginHelp")}
        value={login}
        onChangeText={setLogin}
        error={errors.login}
        autoCapitalize="none"
        autoComplete="username"
        textContentType="username"
        keyboardType="email-address"
        returnKeyType="next"
      />
      <TextField
        label={t("Login.passwordLabel")}
        value={password}
        onChangeText={setPassword}
        error={errors.password}
        password
        autoComplete="current-password"
        textContentType="password"
        returnKeyType="go"
        onSubmitEditing={() => void submit()}
      />

      <Button label={t("Login.submit")} busy={busy} onPress={() => void submit()} />
      <Button
        label={t("Login.forgotPassword")}
        variant="link"
        onPress={() => router.push({ pathname: "/forgot-password", params: passNext(next) })}
      />
      <Button
        label={t("Login.useCode")}
        variant="link"
        onPress={() => router.push({ pathname: "/sign-in-code", params: passNext(next) })}
      />

      <Text variant="muted">{t("Login.noAccount")}</Text>
      <Button
        label={t("Login.createAccount")}
        variant="secondary"
        onPress={() => router.push({ pathname: "/sign-up", params: passNext(next) })}
      />
    </Screen>
  );
}
