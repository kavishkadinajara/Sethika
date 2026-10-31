import { useState } from "react";
import { StyleSheet, View } from "react-native";

import { greetingName, useProfile } from "@/auth/profile-provider";
import { colomboPartOfDay } from "@/bookings/calendar";
import { Bowl } from "@/components/bookings/bowl";
import { InstitutionDirectory } from "@/components/bookings/institution-directory";
import { Screen } from "@/components/screen";
import { Text } from "@/components/text";
import { useI18n } from "@/i18n/i18n-provider";
import { bowl, brand, spacing } from "@/theme/colors";

/** Explore: the listed institutions (plan 03 C10, Phase F slice F.3), open to anyone. */
export default function ExploreScreen() {
  const [pulls, setPulls] = useState(0);
  const [refreshing, setRefreshing] = useState(false);

  return (
    <Screen
      edges={["top"]}
      refreshing={refreshing}
      onRefresh={() => {
        setRefreshing(true);
        setPulls((count) => count + 1);
      }}
    >
      <Welcome />
      <InstitutionDirectory refreshKey={pulls} onSettled={() => setRefreshing(false)} />
    </Screen>
  );
}

/**
 * The brand panel: Estate Green in both themes (DESIGN.md, a place where the brand is the point),
 * a greeting for the time of day in Colombo, and the bowl waiting to be filled.
 */
function Welcome() {
  const { t } = useI18n();
  const name = greetingName(useProfile());
  const greeting = t(`App.explore.greeting.${colomboPartOfDay()}`);

  return (
    <View style={[styles.panel, { backgroundColor: brand.estate }]}>
      <View style={styles.panelText}>
        <Text variant="eyebrow" style={styles.onPanelSoft}>
          {name ? `${greeting}, ${name}` : greeting}
        </Text>
        <Text variant="title" accessibilityRole="header" style={styles.onPanel}>
          {t("App.explore.title")}
        </Text>
        <Text variant="voice" style={styles.onPanelSoft}>
          {t("App.explore.voice")}
        </Text>
      </View>
      <View style={styles.mark}>
        <Bowl fill={0.45} color={brand.gold} scale={2.2} />
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  panel: { ...bowl, flexDirection: "row", alignItems: "flex-end", padding: spacing.lg, gap: spacing.md, marginTop: spacing.sm },
  panelText: { flex: 1, gap: spacing.xs },
  onPanel: { color: brand.ivory },
  // Ivory at 85 %: 6.6:1 on Estate Green (measured), well above 4.5:1.
  onPanelSoft: { color: brand.ivory, opacity: 0.85 },
  mark: { paddingBottom: spacing.xs },
});
