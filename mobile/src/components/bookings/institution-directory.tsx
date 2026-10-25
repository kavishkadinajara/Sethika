import { router } from "expo-router";
import { useCallback, useEffect, useMemo, useState } from "react";
import { Pressable, StyleSheet, View } from "react-native";

import { useBookingApi } from "@/bookings/api";
import { inLocale, localName } from "@/bookings/names";
import type { PublicInstitutionSummary } from "@/bookings/types";
import { Text } from "@/components/text";
import { TextField } from "@/components/text-field";
import { useI18n } from "@/i18n/i18n-provider";
import { radius, spacing } from "@/theme/colors";
import { usePalette } from "@/theme/use-theme";

import { EmptyState, Failed, Loading } from "./states";

/**
 * The institutions that chose to be listed (plan 03, C10), for anyone, signed in or not: a plain
 * list with the web's filter, which looks through every name it shows in all three languages,
 * so a donor can type in any of them. Choosing one opens its page by slug.
 */
export function InstitutionDirectory() {
  const { t, language } = useI18n();
  const api = useBookingApi();
  const palette = usePalette();
  const [institutions, setInstitutions] = useState<PublicInstitutionSummary[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [filter, setFilter] = useState("");

  const load = useCallback(async () => {
    setFailed(false);
    const result = await api.listInstitutions();
    if (result.ok) setInstitutions(result.value);
    else setFailed(true);
  }, [api]);

  useEffect(() => {
    // Loading the list is what this component is for; the state it sets is the fetched data.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load]);

  const shown = useMemo(() => {
    const words = filter.trim().toLocaleLowerCase().split(/\s+/).filter(Boolean);
    if (!institutions || words.length === 0) return institutions;
    return institutions.filter((institution) => {
      const haystack = [
        institution.name, institution.nameSi, institution.nameTa,
        institution.typeName, institution.typeNameSi, institution.typeNameTa,
        institution.district, institution.districtSi, institution.districtTa,
        institution.city,
      ].filter(Boolean).join(" ").toLocaleLowerCase();
      return words.every((word) => haystack.includes(word));
    });
  }, [institutions, filter]);

  if (failed) return <Failed message={t("Discover.loadFailed")} onRetry={() => void load()} />;
  if (institutions === null || shown === null) return <Loading />;
  if (institutions.length === 0) return <EmptyState message={t("Discover.noneListed")} />;

  return (
    <>
      <TextField
        label={t("Discover.filter")}
        help={t("Discover.filterHint")}
        value={filter}
        onChangeText={setFilter}
        autoCorrect={false}
        autoCapitalize="none"
        returnKeyType="search"
      />

      {shown.length === 0 ? (
        <EmptyState message={t("Discover.noneMatch")} />
      ) : (
        <View style={styles.list}>
          {shown.map((institution) => {
            const name = localName(language, institution);
            const type = inLocale(language, institution.typeName, institution.typeNameSi, institution.typeNameTa);
            const district = inLocale(language, institution.district, institution.districtSi, institution.districtTa);
            return (
              <Pressable
                key={institution.slug}
                accessibilityRole="link"
                accessibilityLabel={`${name}. ${type}. ${institution.city}, ${district}`}
                onPress={() => router.push({ pathname: "/i/[slug]", params: { slug: institution.slug } })}
                style={({ pressed }) => [
                  styles.card,
                  { backgroundColor: pressed ? palette.subtle : palette.surface, borderColor: palette.hairline },
                ]}
              >
                <Text variant="heading" style={{ color: palette.brand }}>
                  {name}
                </Text>
                <Text variant="muted">{type}</Text>
                <Text>
                  {institution.city} · {district}
                </Text>
              </Pressable>
            );
          })}
        </View>
      )}
    </>
  );
}

const styles = StyleSheet.create({
  list: { gap: spacing.sm },
  card: { borderWidth: 1, borderRadius: radius.md, padding: spacing.md, gap: spacing.xs, minHeight: 44 },
});
