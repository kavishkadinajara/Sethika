import { Card, Screen } from "@/components/screen";
import { Text } from "@/components/text";
import { useI18n } from "@/i18n/i18n-provider";

/** My bookings. Filled from GET /api/bookings/mine once plan 03 merges (Phase F slice F.4). */
export default function BookingsScreen() {
  const { t } = useI18n();
  return (
    <Screen edges={["top"]}>
      <Text variant="title" accessibilityRole="header">
        {t("App.bookings.title")}
      </Text>
      <Card>
        <Text variant="muted">{t("App.bookings.comingSoon")}</Text>
      </Card>
    </Screen>
  );
}
