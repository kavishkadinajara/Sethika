import { NativeTabs } from "expo-router/native-tabs";
import { useEffect } from "react";

import { ProfileProvider, useProfile } from "@/auth/profile-provider";
import { useI18n } from "@/i18n/i18n-provider";
import { isLanguage } from "@/i18n/translate";
import { tabBarLook } from "@/theme/tab-bar";
import { usePalette } from "@/theme/use-theme";

export default function AppLayout() {
  const palette = usePalette();
  const { t, language } = useI18n();

  return (
    <ProfileProvider>
      <AccountLanguage />
      <NativeTabs {...tabBarLook(palette, language)}>
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
    </ProfileProvider>
  );
}

/**
 * After sign-in the account's PreferredLanguage wins over this phone's, as on the web. The tabs
 * are open to visitors too, so this waits for the profile (and runs again after a sign-in).
 */
function AccountLanguage() {
  const profile = useProfile();
  const { setLanguage } = useI18n();
  const preferred = profile?.preferredLanguage;

  useEffect(() => {
    if (isLanguage(preferred)) setLanguage(preferred);
  }, [preferred, setLanguage]);

  return null;
}
