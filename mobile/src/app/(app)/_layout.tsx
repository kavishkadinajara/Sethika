import { NativeTabs } from "expo-router/native-tabs";
import { useEffect } from "react";

import { useSessionClient } from "@/auth/session-provider";
import { useI18n } from "@/i18n/i18n-provider";
import { isLanguage } from "@/i18n/translate";
import { usePalette } from "@/theme/use-theme";

export default function AppLayout() {
  const palette = usePalette();
  const { t } = useI18n();
  useAccountLanguage();

  return (
    <NativeTabs
      backgroundColor={palette.surface}
      indicatorColor={palette.subtle}
      labelStyle={{ selected: { color: palette.primary } }}
    >
      <NativeTabs.Trigger name="index">
        <NativeTabs.Trigger.Label>{t("App.tabs.explore")}</NativeTabs.Trigger.Label>
        <NativeTabs.Trigger.Icon sf="magnifyingglass" md="search" />
      </NativeTabs.Trigger>
      <NativeTabs.Trigger name="bookings">
        <NativeTabs.Trigger.Label>{t("App.tabs.bookings")}</NativeTabs.Trigger.Label>
        <NativeTabs.Trigger.Icon sf="calendar" md="event" />
      </NativeTabs.Trigger>
      <NativeTabs.Trigger name="profile">
        <NativeTabs.Trigger.Label>{t("App.tabs.profile")}</NativeTabs.Trigger.Label>
        <NativeTabs.Trigger.Icon sf="person.crop.circle" md="account_circle" />
      </NativeTabs.Trigger>
    </NativeTabs>
  );
}

/** After sign-in the account's PreferredLanguage wins over this phone's, as on the web. */
function useAccountLanguage() {
  const client = useSessionClient();
  const { setLanguage } = useI18n();

  useEffect(() => {
    let cancelled = false;
    client
      .fetch("/api/me")
      .then(async (response) => {
        if (!response.ok) return;
        const profile = (await response.json()) as { preferredLanguage?: unknown };
        if (!cancelled && isLanguage(profile.preferredLanguage)) setLanguage(profile.preferredLanguage);
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
  }, [client, setLanguage]);
}
