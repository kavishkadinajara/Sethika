import { useState } from "react";

import { useSession } from "@/auth/session-provider";
import { MyBookings } from "@/components/bookings/my-bookings";
import { Screen, ScreenHeader } from "@/components/screen";
import { useI18n } from "@/i18n/i18n-provider";

/** My bookings: GET /api/bookings/mine, with cancel (Phase F slice F.4). Asks a visitor to sign in. */
export default function BookingsScreen() {
  const { t } = useI18n();
  const [pulls, setPulls] = useState(0);
  const [refreshing, setRefreshing] = useState(false);
  // Only a donor's own list can be pulled: a visitor or an institution session has none to fetch.
  const session = useSession();
  const donor = session.status === "authenticated" && !session.activeInstitution;

  return (
    <Screen
      edges={["top"]}
      refreshing={refreshing}
      onRefresh={
        donor
          ? () => {
              setRefreshing(true);
              setPulls((count) => count + 1);
            }
          : undefined
      }
    >
      <ScreenHeader title={t("App.bookings.title")} voice={t("MyBookings.intro")} />
      <MyBookings refreshKey={pulls} onSettled={() => setRefreshing(false)} />
    </Screen>
  );
}
