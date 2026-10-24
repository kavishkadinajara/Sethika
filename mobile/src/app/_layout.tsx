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
      <I18nProvider>
        <SessionProvider>
          <StatusBar style="auto" />
          <Routes />
        </SessionProvider>
      </I18nProvider>
    </ThemeProvider>
  );
}

/*  The splash stays up until the fonts are in and the stored session has been tried, so the
    first screen a donor sees is the right one - never a flash of sign-in before their tabs.
    Signed-in and signed-out screens are separate route groups behind Stack.Protected: a
    signed-out user cannot be routed into (app), and signing out drops them back to sign-in.  */
function Routes() {
  const session = useSession();
  const [fontsLoaded, fontError] = useFonts({ Outfit_400Regular, Outfit_500Medium, Outfit_600SemiBold });
  const ready = (fontsLoaded || fontError !== null) && session.status !== "loading";

  useEffect(() => {
    if (ready) void SplashScreen.hideAsync();
  }, [ready]);

  if (!ready) return null;
  if (session.status === "unavailable") return <Unavailable />;

  const signedIn = session.status === "authenticated";
  return (
    <Stack screenOptions={{ headerShown: false }}>
      <Stack.Protected guard={signedIn} redirectTo="/sign-in">
        <Stack.Screen name="(app)" />
      </Stack.Protected>
      <Stack.Protected guard={!signedIn} redirectTo="/">
        <Stack.Screen name="(auth)" />
      </Stack.Protected>
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
