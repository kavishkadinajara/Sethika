import { router } from "expo-router";
import { type ReactNode, useEffect, useState } from "react";
import { Linking, Pressable, StyleSheet, View } from "react-native";

import { useBookingApi } from "@/bookings/api";
import { localName } from "@/bookings/names";
import { nativeLocation } from "@/bookings/native-location";
import {
  DEFAULT_RADIUS_KM,
  formatKm,
  locate,
  nearDate,
  RADII_KM,
  type LocationDevice,
  type LocationProblem,
  type NearDay,
  type NearPoint,
  type RadiusKm,
} from "@/bookings/near-me";
import type { SearchAnswer, SearchPlace, SearchSlot } from "@/bookings/types";
import { useDates } from "@/bookings/use-dates";
import { Button } from "@/components/button";
import { Icon } from "@/components/icon";
import { FormError } from "@/components/screen";
import { Text } from "@/components/text";
import { useI18n } from "@/i18n/i18n-provider";
import { cup, spacing } from "@/theme/colors";
import { usePalette } from "@/theme/use-theme";

import { Chip, ChipRow, InstitutionCard } from "./institution-directory";
import { NearMap } from "./near-map";
import { EmptyState, Failed, Loading } from "./states";

type Search = { point: NearPoint; day: NearDay; radiusKm: RadiusKm };

type Props = {
  /** The directory: shown until a position is found, and whenever the donor goes back to it. */
  children: ReactNode;
  /** Changes when the donor pulls to refresh: the search runs again from the same point. */
  refreshKey?: number;
  /** A search finished, well or not: the pull-to-refresh spinner can stop. */
  onSettled?: () => void;
  device?: LocationDevice;
};

/**
 * Near me (F33). The phone asks for its position only when the donor taps, and the rounded point
 * stays in this component until they leave the list. A refusal leaves the directory where it was
 * and says why. The list is places with a meal open that day, not every place, and it says so.
 * Tomorrow comes first, as on the web: most meals are booked a day ahead at the earliest.
 */
export function NearMe({ children, refreshKey = 0, onSettled, device = nativeLocation }: Props) {
  const { t } = useI18n();
  const api = useBookingApi();
  const [locating, setLocating] = useState(false);
  const [problem, setProblem] = useState<LocationProblem | null>(null);
  const [search, setSearch] = useState<Search | null>(null);
  const [answer, setAnswer] = useState<SearchAnswer | null>(null);
  const [failed, setFailed] = useState(false);
  const [attempts, setAttempts] = useState(0);

  useEffect(() => {
    if (!search) return;
    // A slower answer to an earlier choice must not land on top of the one now chosen.
    let current = true;
    void api.searchNear(nearDate(search.day), search.point, search.radiusKm).then((result) => {
      if (!current) return;
      if (result.ok) {
        setAnswer(result.value);
        setFailed(false);
      } else {
        setFailed(true);
      }
      onSettled?.();
    });
    return () => {
      current = false;
    };
    // A pull or a retry asks again; a new onSettled callback on its own must not.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [api, search, refreshKey, attempts]);

  async function findMe() {
    setProblem(null);
    setLocating(true);
    const located = await locate(device);
    setLocating(false);
    if (located.ok) setSearch({ point: located.point, day: "tomorrow", radiusKm: DEFAULT_RADIUS_KM });
    else setProblem(located.problem);
  }

  function narrow(next: Search) {
    setAnswer(null);
    setFailed(false);
    setSearch(next);
  }

  function retry() {
    setFailed(false);
    setAttempts((count) => count + 1);
  }

  function showEveryPlace() {
    setSearch(null);
    setAnswer(null);
    setFailed(false);
  }

  if (!search) {
    return (
      <>
        <Button
          label={t(locating ? "App.near.locating" : "App.near.button")}
          icon="place"
          variant="secondary"
          busy={locating}
          onPress={() => void findMe()}
        />
        {problem && <FormError message={t(`App.near.${problem}`)} />}
        {problem === "denied" && <Button label={t("App.near.openSettings")} variant="quiet" onPress={() => void Linking.openSettings()} />}
        {children}
      </>
    );
  }

  return (
    <View style={styles.near}>
      <ChipRow label={t("App.near.day")}>
        {(["today", "tomorrow"] as const).map((day) => (
          <Chip key={day} label={t(`App.near.${day}`)} selected={search.day === day} onPress={() => narrow({ ...search, day })} />
        ))}
      </ChipRow>
      <ChipRow label={t("App.near.radius")}>
        {RADII_KM.map((radiusKm) => (
          <Chip
            key={radiusKm}
            label={t("App.near.km", { km: String(radiusKm) })}
            selected={search.radiusKm === radiusKm}
            onPress={() => narrow({ ...search, radiusKm })}
          />
        ))}
      </ChipRow>

      {failed ? (
        <Failed message={t("App.near.failed")} onRetry={retry} />
      ) : answer === null ? (
        <Loading />
      ) : (
        <Results answer={answer} point={search.point} radiusKm={search.radiusKm} />
      )}

      <Button label={t("App.near.showAll")} variant="quiet" onPress={showEveryPlace} />
    </View>
  );
}

function Results({ answer, point, radiusKm }: { answer: SearchAnswer; point: NearPoint; radiusKm: RadiusKm }) {
  const { t } = useI18n();
  const dates = useDates();
  const [mapOpen, setMapOpen] = useState(false);
  const date = dates.full(answer.date);

  return (
    <>
      <View style={styles.heading}>
        <Text variant="heading" accessibilityRole="header">
          {t("App.near.title", { date })}
        </Text>
        <Text variant="muted">{t("App.near.intro")}</Text>
      </View>

      {answer.results.length === 0 ? (
        <EmptyState message={t("App.near.none", { date })} />
      ) : (
        <>
          <Text variant="caption" accessibilityLiveRegion="polite">
            {t("App.near.count", { count: answer.results.length })}
            {answer.more ? ` ${t("App.near.moreNearest")}` : ""}
          </Text>
          <Button label={t("App.near.map")} icon="place" variant="secondary" onPress={() => setMapOpen(true)} />
          {answer.results.map((place, index) => (
            <Place key={place.slug} place={place} number={index + 1} date={answer.date} />
          ))}
          {mapOpen && <NearMap places={answer.results} point={point} radiusKm={radiusKm} onClose={() => setMapOpen(false)} />}
        </>
      )}
    </>
  );
}

function Place({ place, number, date }: { place: SearchPlace; number: number; date: string }) {
  const { t } = useI18n();
  return (
    <View style={styles.place}>
      <InstitutionCard
        institution={place}
        number={number}
        distance={place.distanceKm === null ? undefined : t("App.near.distance", { km: formatKm(place.distanceKm) })}
      />
      {place.slots.map((slot) => (
        <Meal key={slot.mealSlot} place={place} slot={slot} date={date} />
      ))}
    </View>
  );
}

/** A meal open that day. A tap opens the place's booking sheet on this meal and this day. */
function Meal({ place, slot, date }: { place: SearchPlace; slot: SearchSlot; date: string }) {
  const { t, language } = useI18n();
  const palette = usePalette();
  const meal = localName(language, slot);

  return (
    <Pressable
      accessibilityRole="link"
      accessibilityLabel={t("App.near.bookLabel", { meal, place: localName(language, place) })}
      onPress={() => router.push({ pathname: "/i/[slug]", params: { slug: place.slug, slot: slot.mealSlot, date } })}
      style={({ pressed }) => [styles.meal, { backgroundColor: pressed ? palette.hairline : palette.subtle }]}
    >
      <View style={styles.grow}>
        <Text variant="label">
          {meal} · {slot.startTime}–{slot.endTime}
        </Text>
        <Text variant="muted">{t("App.near.left", { count: slot.sponsors })}</Text>
      </View>
      <Icon name="forward" size={18} color={palette.line} />
    </Pressable>
  );
}

const styles = StyleSheet.create({
  near: { gap: spacing.sm + 4 },
  heading: { gap: spacing.xs },
  place: { gap: spacing.xs },
  grow: { flex: 1 },
  meal: { ...cup, flexDirection: "row", alignItems: "center", gap: spacing.sm, minHeight: 44, paddingHorizontal: spacing.md, paddingVertical: spacing.sm },
});
