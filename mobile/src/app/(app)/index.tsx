import { InstitutionDirectory } from "@/components/bookings/institution-directory";
import { Screen } from "@/components/screen";
import { Text } from "@/components/text";
import { useI18n } from "@/i18n/i18n-provider";

/** Explore: the listed institutions (plan 03 C10, Phase F slice F.3), open to anyone. */
export default function ExploreScreen() {
  const { t } = useI18n();
  return (
    <Screen edges={["top"]}>
      <Text variant="title" accessibilityRole="header">
        {t("App.explore.title")}
      </Text>
      <Text variant="muted">{t("Discover.directoryIntro")}</Text>
      <InstitutionDirectory />
    </Screen>
  );
}
