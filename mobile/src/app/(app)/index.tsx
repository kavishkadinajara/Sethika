import { Card, Screen } from "@/components/screen";
import { Text } from "@/components/text";
import { useI18n } from "@/i18n/i18n-provider";

/** Explore. Institutions and their slots arrive with plan 03's donor API (Phase F slice F.3). */
export default function ExploreScreen() {
  const { t } = useI18n();
  return (
    <Screen edges={["top"]}>
      <Text variant="title" accessibilityRole="header">
        {t("App.explore.title")}
      </Text>
      <Card>
        <Text variant="muted">{t("App.explore.comingSoon")}</Text>
      </Card>
    </Screen>
  );
}
