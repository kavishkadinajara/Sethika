import { useState } from "react";

import { usePermissions } from "@/auth/permission-provider";
import { useSession } from "@/auth/session-provider";
import { mayOpenToday } from "@/bookings/today";
import { Screen } from "@/components/screen";
import { TodayView } from "@/components/today/today-view";

/** Today: the active institution's meals and sponsors (Phase F slice F.7). */
export default function TodayScreen() {
  const [pulls, setPulls] = useState(0);
  const [refreshing, setRefreshing] = useState(false);
  const session = useSession();
  const { can } = usePermissions();
  const institution = session.status === "authenticated" ? session.activeInstitution : null;

  return (
    <Screen
      edges={["top"]}
      refreshing={refreshing}
      onRefresh={
        mayOpenToday(session, can)
          ? () => {
              setRefreshing(true);
              setPulls((count) => count + 1);
            }
          : undefined
      }
    >
      {/* Keyed: one institution's sponsors must never show, or be served, under another's name. */}
      <TodayView key={institution} refreshKey={pulls} onSettled={() => setRefreshing(false)} />
    </Screen>
  );
}
