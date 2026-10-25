import { useLocalSearchParams } from "expo-router";

import { chosenDay } from "@/bookings/return-path";
import { InstitutionPage } from "@/components/bookings/institution-page";
import { Screen } from "@/components/screen";

/**
 * /i/{slug}, the web's address for an institution's own page (plan 03, C10), open to anyone.
 * `?slot={publicId}&date=yyyy-MM-dd` opens the booking sheet on that meal and day: a shared link,
 * or the donor back from signing in (E33). Only public ids ever travel in the route.
 */
export default function InstitutionScreen() {
  const params = useLocalSearchParams<{ slug: string; slot?: string; date?: string }>();
  return (
    <Screen edges={["bottom"]}>
      <InstitutionPage key={params.slug} slug={params.slug} chosen={chosenDay(params)} />
    </Screen>
  );
}
