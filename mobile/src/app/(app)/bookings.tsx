import { MyBookings } from "@/components/bookings/my-bookings";
import { Screen } from "@/components/screen";
import { Text } from "@/components/text";
import { useI18n } from "@/i18n/i18n-provider";

/** My bookings: GET /api/bookings/mine, with cancel (Phase F slice F.4). Asks a visitor to sign in. */
export default function BookingsScreen() {
  const { t } = useI18n();
  return (
    <Screen edges={["top"]}>
      <Text variant="title" accessibilityRole="header">
        {t("App.bookings.title")}
      </Text>
      <MyBookings />
    </Screen>
  );
}
