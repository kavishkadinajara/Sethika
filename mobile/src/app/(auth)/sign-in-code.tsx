import { router, useLocalSearchParams } from "expo-router";
import { useState } from "react";

import { describeFailure, networkFailure, postJson } from "@/api/http";
import { leaveSignIn } from "@/auth/leave-sign-in";
import { useSessionClient } from "@/auth/session-provider";
import { Button } from "@/components/button";
import { CodeStep, PhoneStep } from "@/components/code-steps";
import { BrandHeader, FormError, Screen } from "@/components/screen";
import { Text } from "@/components/text";
import { useI18n } from "@/i18n/i18n-provider";

/** Sign in with a texted code: /api/auth/otp/request (purpose login), then /api/auth/mobile/otp/sign-in. */
export default function SignInWithCodeScreen() {
  const { t } = useI18n();
  const client = useSessionClient();
  const { next } = useLocalSearchParams<{ next?: string }>();
  const [phone, setPhone] = useState<string | null>(null);
  const [failure, setFailure] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function run(work: () => Promise<void>) {
    setBusy(true);
    setFailure(null);
    try {
      await work();
    } catch {
      setFailure(networkFailure(t).message);
    } finally {
      setBusy(false);
    }
  }

  const requestCode = (number: string) =>
    run(async () => {
      // The answer is the same whether or not the number has an account (OtpSignInHandler).
      const response = await postJson("/api/auth/otp/request", { phoneNumber: number, purpose: "login" });
      if (response.ok) setPhone(number);
      else setFailure((await describeFailure(response, t)).message);
    });

  const signIn = (code: string) =>
    run(async () => {
      const response = await postJson("/api/auth/mobile/otp/sign-in", { phoneNumber: phone, code });
      if (response.ok) {
        await client.establish(response);
        leaveSignIn(next);
      } else {
        setFailure((await describeFailure(response, t)).message);
      }
    });

  return (
    <Screen>
      <BrandHeader />
      <Text variant="title" accessibilityRole="header">
        {phone ? t("LoginOtp.codeTitle") : t("LoginOtp.title")}
      </Text>

      <FormError message={failure} />

      {phone ? (
        <>
          <Text variant="muted">{t("LoginOtp.codeSentTo", { phone })}</Text>
          <CodeStep
            submitLabel={t("LoginOtp.signIn")}
            busy={busy}
            onSubmit={(code) => void signIn(code)}
            onResend={() => void requestCode(phone)}
            onChangeNumber={() => setPhone(null)}
          />
        </>
      ) : (
        <>
          <PhoneStep
            label={t("LoginOtp.phoneLabel")}
            help={t("LoginOtp.phoneHelp")}
            requiredMessage={t("LoginOtp.phoneRequired")}
            submitLabel={t("LoginOtp.sendCode")}
            busy={busy}
            onSubmit={(number) => void requestCode(number)}
          />
          <Button label={t("LoginOtp.usePassword")} variant="link" onPress={() => router.back()} />
        </>
      )}
    </Screen>
  );
}
