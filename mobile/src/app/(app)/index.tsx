import { useState } from "react";

import { DayFrame } from "@/components/bookings/day-frame";
import { InstitutionDirectory } from "@/components/bookings/institution-directory";
import { NearMe } from "@/components/bookings/near-me";
import { Screen, SectionTitle } from "@/components/screen";
import { useI18n } from "@/i18n/i18n-provider";

/** Explore: the day first (F22), then the places (plan 03 C10, Phase F slice F.3), open to anyone. */
export default function ExploreScreen() {
  const { t } = useI18n();
  const [pulls, setPulls] = useState(0);
  const [refreshing, setRefreshing] = useState(false);
  const settled = () => setRefreshing(false);

  return (
    <Screen
      edges={["top"]}
      refreshing={refreshing}
      onRefresh={() => {
        setRefreshing(true);
        setPulls((count) => count + 1);
      }}
    >
      <DayFrame refreshKey={pulls} />
      <SectionTitle>{t("App.explore.title")}</SectionTitle>
      <NearMe refreshKey={pulls} onSettled={settled}>
        <InstitutionDirectory refreshKey={pulls} onSettled={settled} />
      </NearMe>
    </Screen>
  );
}
