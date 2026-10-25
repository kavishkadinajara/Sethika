import { router } from "expo-router";
import { useEffect, useState } from "react";
import { Pressable, StyleSheet, View } from "react-native";

import { useSession, useSessionClient } from "@/auth/session-provider";
import { Button } from "@/components/button";
import { Card, Screen } from "@/components/screen";
import { Text } from "@/components/text";
import { useI18n } from "@/i18n/i18n-provider";
import { LANGUAGES, type Language } from "@/i18n/translate";
import { radius, spacing } from "@/theme/colors";
import { usePalette } from "@/theme/use-theme";

export default function ProfileScreen() {
  const { t } = useI18n();
  const session = useSession();
  const client = useSessionClient();
  const signedIn = session.status === "authenticated";
  const [name, setName] = useState<string | null>(null);
  const [busy, setBusy] = useState<"logout" | "logout-all" | null>(null);

  useEffect(() => {
    if (!signedIn) return;
    let cancelled = false;
    client
      .fetch("/api/me")
      .then(async (response) => {
        if (!response.ok) return;
        const profile = (await response.json()) as { displayName?: string; fullName?: string };
        if (!cancelled) setName(profile.displayName || profile.fullName || null);
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
  }, [client, signedIn]);

  const active =
    session.status === "authenticated"
      ? session.institutions.find((institution) => institution.slug === session.activeInstitution)
      : undefined;

  async function end(kind: "logout" | "logout-all") {
    setBusy(kind);
    // Signing out flips the session to anonymous; this screen then shows the sign-in prompts.
    await (kind === "logout" ? client.logout() : client.logoutAll());
    setName(null);
    setBusy(null);
  }

  return (
    <Screen edges={["top"]}>
      <Text variant="title" accessibilityRole="header">
        {t("App.profile.title")}
      </Text>
      {signedIn && name && <Text variant="heading">{t("App.profile.greeting", { name })}</Text>}
      {active && <Text variant="muted">{t("Session.actingFor", { name: active.name })}</Text>}

      <Card>
        <LanguagePicker signedIn={signedIn} />
      </Card>

      {!signedIn ? (
        <Card>
          <Text>{t("App.profile.signedOut")}</Text>
          <Button label={t("Login.submit")} onPress={() => router.push("/sign-in")} />
          <Button label={t("Login.createAccount")} variant="secondary" onPress={() => router.push("/sign-up")} />
        </Card>
      ) : (
        <Card>
          <Button
            label={t("Session.signOut")}
            variant="secondary"
            busy={busy === "logout"}
            disabled={busy !== null}
            onPress={() => void end("logout")}
          />
          <Text variant="muted">{t("App.profile.signOutEverywhereHelp")}</Text>
          <Button
            label={t("Session.signOutEverywhere")}
            variant="secondary"
            busy={busy === "logout-all"}
            disabled={busy !== null}
            onPress={() => void end("logout-all")}
          />
        </Card>
      )}
    </Screen>
  );
}

/** Changes the app's language at once, and a signed-in account's, so SMS follow it too. */
function LanguagePicker({ signedIn }: { signedIn: boolean }) {
  const { t, language, setLanguage } = useI18n();
  const client = useSessionClient();
  const palette = usePalette();

  function choose(next: Language) {
    if (next === language) return;
    setLanguage(next);
    if (!signedIn) return;
    void client
      .fetch("/api/me/language", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ language: next }),
      })
      .catch(() => {});
  }

  return (
    <View style={styles.picker} accessibilityRole="radiogroup" accessibilityLabel={t("LanguageSwitcher.label")}>
      <Text variant="label">{t("LanguageSwitcher.label")}</Text>
      <View style={styles.options}>
        {LANGUAGES.map((code) => {
          const selected = code === language;
          return (
            <Pressable
              key={code}
              accessibilityRole="radio"
              accessibilityState={{ selected }}
              onPress={() => choose(code)}
              style={[
                styles.option,
                {
                  borderColor: selected ? palette.primary : palette.line,
                  backgroundColor: selected ? palette.primary : palette.surface,
                },
              ]}
            >
              <Text style={{ color: selected ? palette.onPrimary : palette.fg }}>
                {t(`LanguageSwitcher.names.${code}`)}
              </Text>
            </Pressable>
          );
        })}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  picker: { gap: spacing.sm },
  options: { flexDirection: "row", flexWrap: "wrap", gap: spacing.sm },
  option: {
    borderWidth: 1,
    borderRadius: radius.sm,
    paddingVertical: spacing.sm,
    paddingHorizontal: spacing.md,
    minHeight: 44,
    justifyContent: "center",
  },
});
