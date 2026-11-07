import { router } from "expo-router";
import { type ReactNode, useCallback, useEffect, useMemo, useState } from "react";
import { Pressable, ScrollView, StyleSheet, View } from "react-native";

import { useBookingApi } from "@/bookings/api";
import { inLocale, localName } from "@/bookings/names";
import type { PublicInstitutionSummary } from "@/bookings/types";
import { Icon } from "@/components/icon";
import { Text } from "@/components/text";
import { TextField } from "@/components/text-field";
import { useI18n } from "@/i18n/i18n-provider";
import { bowl, cup, radius, spacing } from "@/theme/colors";
import { useLift, usePalette } from "@/theme/use-theme";

import { EmptyState, Failed, Loading } from "./states";

type Props = {
  /** Changes when the donor pulls to refresh: the list is fetched again. */
  refreshKey?: number;
  /** A fetch finished, well or not: the pull-to-refresh spinner can stop. */
  onSettled?: () => void;
};

/**
 * The institutions that chose to be listed (plan 03, C10), for anyone, signed in or not. The
 * search looks through every name it shows in all three languages, so a donor can type in any
 * of them; the chips narrow the list to one kind of place, in words only (faith-neutral: no
 * symbol stands for a faith). Choosing one opens its page by slug.
 */
export function InstitutionDirectory({ refreshKey = 0, onSettled }: Props) {
  const { t, language } = useI18n();
  const api = useBookingApi();
  const [institutions, setInstitutions] = useState<PublicInstitutionSummary[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [filter, setFilter] = useState("");
  const [kind, setKind] = useState<string | null>(null);

  const load = useCallback(async () => {
    setFailed(false);
    const result = await api.listInstitutions();
    if (result.ok) setInstitutions(result.value);
    else setFailed(true);
    onSettled?.();
  }, [api, onSettled]);

  useEffect(() => {
    // Loading the list is what this component is for; the state it sets is the fetched data.
    // A pull asks again (refreshKey); a new onSettled callback on its own must not.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [api, refreshKey]);

  // The kinds of place listed, each once, in the order they first appear.
  const kinds = useMemo(() => {
    const seen = new Map<string, string>();
    for (const institution of institutions ?? []) {
      if (!seen.has(institution.type)) {
        seen.set(institution.type, inLocale(language, institution.typeName, institution.typeNameSi, institution.typeNameTa));
      }
    }
    return [...seen.entries()].map(([code, name]) => ({ code, name }));
  }, [institutions, language]);

  const shown = useMemo(() => {
    const words = filter.trim().toLocaleLowerCase().split(/\s+/).filter(Boolean);
    if (!institutions) return institutions;
    return institutions.filter((institution) => {
      if (kind && institution.type !== kind) return false;
      if (words.length === 0) return true;
      const haystack = [
        institution.name, institution.nameSi, institution.nameTa,
        institution.typeName, institution.typeNameSi, institution.typeNameTa,
        institution.district, institution.districtSi, institution.districtTa,
        institution.city,
      ].filter(Boolean).join(" ").toLocaleLowerCase();
      return words.every((word) => haystack.includes(word));
    });
  }, [institutions, filter, kind]);

  if (failed) return <Failed message={t("Discover.loadFailed")} onRetry={() => void load()} />;
  if (institutions === null || shown === null) return <Loading />;
  if (institutions.length === 0) return <EmptyState message={t("Discover.noneListed")} />;

  return (
    <>
      <TextField
        label={t("Discover.filter")}
        hideLabel
        icon="search"
        placeholder={t("Discover.filterHint")}
        value={filter}
        onChangeText={setFilter}
        autoCorrect={false}
        autoCapitalize="none"
        returnKeyType="search"
        clearButtonMode="while-editing"
      />

      {kinds.length > 1 && (
        <ChipRow label={t("App.explore.kinds")}>
          <Chip label={t("App.explore.allKinds")} selected={kind === null} onPress={() => setKind(null)} />
          {kinds.map((option) => (
            <Chip key={option.code} label={option.name} selected={kind === option.code} onPress={() => setKind(option.code)} />
          ))}
        </ChipRow>
      )}

      {shown.length === 0 ? (
        <EmptyState message={t("Discover.noneMatch")} />
      ) : (
        <View style={styles.list}>
          <Text variant="caption" accessibilityLiveRegion="polite">
            {t("App.explore.count", { count: shown.length })}
          </Text>
          {shown.map((institution) => (
            <InstitutionCard key={institution.slug} institution={institution} />
          ))}
        </View>
      )}
    </>
  );
}

/** A place's card. `distance` is the words for how far it is, when the donor searched near a point. */
export function InstitutionCard({ institution, distance }: { institution: PublicInstitutionSummary; distance?: string }) {
  const { language } = useI18n();
  const palette = usePalette();
  const lift = useLift();
  const name = localName(language, institution);
  const type = inLocale(language, institution.typeName, institution.typeNameSi, institution.typeNameTa);
  const district = inLocale(language, institution.district, institution.districtSi, institution.districtTa);

  return (
    <Pressable
      accessibilityRole="link"
      accessibilityLabel={[`${name}. ${type}. ${institution.city}, ${district}`, distance].filter(Boolean).join(". ")}
      onPress={() => router.push({ pathname: "/i/[slug]", params: { slug: institution.slug } })}
      style={({ pressed }) => [
        styles.card,
        lift,
        { backgroundColor: pressed ? palette.subtle : palette.surface, borderColor: palette.hairline },
        pressed && styles.pressed,
      ]}
    >
      {/* A monogram in a cup: each place gets a face without a photo or a faith's symbol. */}
      <View style={[styles.monogram, { backgroundColor: palette.subtle, borderColor: palette.hairline }]}>
        <Text variant="heading" style={[styles.monogramText, { color: palette.brand }]}>
          {Array.from(name)[0] ?? ""}
        </Text>
      </View>
      <View style={styles.cardText}>
        <Text variant="eyebrow">{type}</Text>
        <Text variant="heading">{name}</Text>
        <View style={styles.place}>
          <Icon name="place" size={15} />
          <Text variant="muted" style={styles.grow}>
            {institution.city} · {district}
          </Text>
        </View>
        {distance && <Text variant="label">{distance}</Text>}
      </View>
      <Icon name="forward" size={18} color={palette.line} />
    </Pressable>
  );
}

/** One choice as a row of chips, edge to edge, that scrolls sideways on a narrow phone. */
export function ChipRow({ label, children }: { label: string; children: ReactNode }) {
  return (
    <ScrollView
      horizontal
      showsHorizontalScrollIndicator={false}
      contentContainerStyle={styles.chips}
      style={styles.chipRow}
      accessibilityRole="radiogroup"
      accessibilityLabel={label}
    >
      {children}
    </ScrollView>
  );
}

export function Chip({ label, selected, onPress }: { label: string; selected: boolean; onPress: () => void }) {
  const palette = usePalette();
  return (
    <Pressable
      accessibilityRole="radio"
      accessibilityState={{ selected }}
      onPress={onPress}
      style={({ pressed }) => [
        styles.chip,
        {
          backgroundColor: selected ? palette.primary : pressed ? palette.subtle : palette.surface,
          borderColor: selected ? palette.primary : palette.line,
        },
      ]}
    >
      <Text variant="label" style={{ color: selected ? palette.onPrimary : palette.fg }}>
        {label}
      </Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  grow: { flex: 1 },
  list: { gap: spacing.sm + 4 },
  chipRow: { marginHorizontal: -spacing.md, flexGrow: 0 },
  chips: { gap: spacing.sm, paddingHorizontal: spacing.md },
  chip: { minHeight: 44, borderRadius: radius.pill, borderWidth: 1, paddingHorizontal: spacing.md, justifyContent: "center" },
  card: { ...bowl, flexDirection: "row", alignItems: "center", borderWidth: 1, padding: spacing.md, gap: spacing.md, minHeight: 44 },
  pressed: { transform: [{ scale: 0.98 }] },
  monogram: { ...cup, width: 56, height: 60, borderWidth: 1, alignItems: "center", justifyContent: "center" },
  monogramText: { fontSize: 24, lineHeight: 32 },
  cardText: { flex: 1, gap: 2 },
  place: { flexDirection: "row", alignItems: "center", gap: 4, marginTop: 2 },
});
