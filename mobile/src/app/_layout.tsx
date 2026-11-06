import { Outfit_400Regular, Outfit_500Medium, Outfit_600SemiBold, useFonts } from "@expo-google-fonts/outfit";
import { DarkTheme, DefaultTheme, Stack, ThemeProvider } from "expo-router";
import * as SplashScreen from "expo-splash-screen";
import { StatusBar } from "expo-status-bar";
import { useEffect } from "react";
import { StyleSheet, useColorScheme, View } from "react-native";

import { SessionProvider, useSession, useSessionClient } from "@/auth/session-provider";
import { Button } from "@/components/button";
import { BrandHeader, Screen } from "@/components/screen";
import { Text } from "@/components/text";
import { I18nProvider, useI18n } from "@/i18n/i18n-provider";
import { ThemePreferenceProvider, useThemePreference } from "@/theme/appearance";
import { colors, spacing } from "@/theme/colors";

SplashScreen.preventAutoHideAsync();

export default function RootLayout() {
  const scheme = useColorScheme();
  const palette = scheme === "dark" ? colors.dark : colors.light;
  const base = scheme === "dark" ? DarkTheme : DefaultTheme;

  return (
    <ThemeProvider
      value={{
        ...base,
        colors: {
          ...base.colors,
          background: palette.canvas,
          card: palette.surface,
          primary: palette.primary,
          text: palette.fg,
          border: palette.hairline,
        },
      }}
    >
      <ThemePreferenceProvider>
        <I18nProvider>
          <SessionProvider>
            <StatusBar style="auto" />
            <Routes />
          </SessionProvider>
        </I18nProvider>
      </ThemePreferenceProvider>
    </ThemeProvider>
  );
}

/*  The splash stays up until the fonts are in, the saved theme is applied and the stored session
    has been tried, so the first screen shows the right state: the donor's own, or the prompts to
    sign in, in the right colours.

    Anyone may look (plan 03, C10): the tabs and institution pages are open to a visitor who
    never signed in, and each screen that needs an account says so where it is needed (the
    booking sheet, My bookings, Profile). The sign-in screens are pushed on top of whatever the
    visitor was doing and take themselves away when they succeed (auth/leave-sign-in.ts), so a
    donor who signs in to book lands back on the same day. Signing out leaves the donor where
    they are, signed out.                                                                     */
function Routes() {
  const session = useSession();
  const { loaded: themeLoaded } = useThemePreference();
  const scheme = useColorScheme();
  const palette = scheme === "dark" ? colors.dark : colors.light;
  const [fontsLoaded, fontError] = useFonts({
    Outfit_400Regular,
    Outfit_500Medium,
    Outfit_600SemiBold,
    // Fraunces italic, the human voice (DESIGN.md), from the OFL font in assets/fonts: one static
    // face, copied in rather than installed as a package (PHASE-F-MOBILE.md, F16).
    Fraunces_400Regular_Italic: require("@/assets/fonts/Fraunces_400Regular_Italic.ttf"),
  });
  const ready = (fontsLoaded || fontError !== null) && themeLoaded && session.status !== "loading";

  useEffect(() => {
    if (ready) void SplashScreen.hideAsync();
  }, [ready]);

  if (!ready) return null;
  if (session.status === "unavailable") return <Unavailable />;

  return (
    <Stack screenOptions={{ headerShown: false }}>
      <Stack.Screen name="(app)" />
      <Stack.Screen
        name="i/[slug]"
        options={{
          headerShown: true,
          title: "",
          headerShadowVisible: false,
          headerStyle: { backgroundColor: palette.canvas },
          headerTintColor: palette.primary,
        }}
      />
      <Stack.Screen name="(auth)" />
    </Stack>
  );
}

/** The API could not be reached at launch. The stored session is kept; trying again may restore it. */
function Unavailable() {
  const { t } = useI18n();
  const client = useSessionClient();
  return (
    <Screen>
      <View style={styles.centre}>
        <BrandHeader />
        <Text>{t("Session.unavailable")}</Text>
        <Button label={t("Signup.retry")} onPress={() => void client.retry()} />
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  centre: { flex: 1, justifyContent: "center", gap: spacing.md },
});
