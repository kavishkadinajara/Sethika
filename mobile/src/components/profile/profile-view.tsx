import Constants from "expo-constants";
import { router, useFocusEffect } from "expo-router";
import { useCallback, useEffect, useState, type ReactNode } from "react";
import { AppState, Image, Linking, Pressable, StyleSheet, Switch, View } from "react-native";

import { greetingName, initials, useProfile } from "@/auth/profile-provider";
import { useSession, useSessionClient } from "@/auth/session-provider";
import { useBookingApi } from "@/bookings/api";
import { colomboToday } from "@/bookings/calendar";
import { donorStats, type DonorStats } from "@/bookings/mine";
import { inLocale } from "@/bookings/names";
import { useDates } from "@/bookings/use-dates";
import { Bowl } from "@/components/bookings/bowl";
import { Button } from "@/components/button";
import { Icon, type IconName } from "@/components/icon";
import { Card, SectionTitle } from "@/components/screen";
import { Text } from "@/components/text";
import { useI18n } from "@/i18n/i18n-provider";
import { LANGUAGES, type Language } from "@/i18n/translate";
import type { PushStatus } from "@/notifications/push";
import { useChannelName, usePush } from "@/notifications/push-provider";
import { THEMES, useThemePreference, type ThemeChoice } from "@/theme/appearance";
import { bowl, brand, radius, spacing } from "@/theme/colors";
import { usePalette } from "@/theme/use-theme";

/**
 * Profile: who is signed in and what their giving adds up to (from their own bookings), the
 * phone's preferences (language, appearance), and the ways out. A visitor sees what an account
 * is for, the same preferences, and the ways in.
 */
export function ProfileView() {
  const session = useSession();
  const signedIn = session.status === "authenticated";

  return (
    <>
      {signedIn ? <Account /> : <Welcome />}
      <Preferences signedIn={signedIn} />
      {signedIn && <SignOut />}
      <About />
    </>
  );
}

function Account() {
  const { t } = useI18n();
  const session = useSession();
  const profile = useProfile();
  const palette = usePalette();
  const name = greetingName(profile);
  const active =
    session.status === "authenticated"
      ? session.institutions.find((institution) => institution.slug === session.activeInstitution)
      : undefined;

  return (
    <>
      <View style={styles.hero}>
        <View style={[styles.avatarRing, { borderColor: palette.accent }]}>
          <View style={[styles.avatar, { backgroundColor: palette.primary }]}>
            <Text variant="title" style={{ color: palette.onPrimary }}>
              {initials(profile) || "·"}
            </Text>
          </View>
        </View>
        <View style={styles.heroText}>
          <Text variant="eyebrow">{t("App.profile.title")}</Text>
          <Text variant="title" accessibilityRole="header">
            {name ? t("App.profile.greeting", { name }) : t("App.profile.title")}
          </Text>
          {profile?.fullName && profile.fullName !== name && <Text variant="muted">{profile.fullName}</Text>}
        </View>
      </View>

      {active ? (
        <View style={[styles.acting, { backgroundColor: palette.subtle, borderColor: palette.hairline }]}>
          <Icon name="person" size={16} color={palette.primary} />
          <Text variant="label" style={styles.grow}>
            {t("Session.actingFor", { name: active.name })}
          </Text>
        </View>
      ) : (
        <Giving />
      )}
    </>
  );
}

/** What the donor's bookings add up to, and the next one. Fetched each time the tab is shown. */
function Giving() {
  const { t, language } = useI18n();
  const api = useBookingApi();
  const dates = useDates();
  const palette = usePalette();
  const [stats, setStats] = useState<DonorStats | null>(null);

  useFocusEffect(
    useCallback(() => {
      let cancelled = false;
      void api.listMine().then((result) => {
        if (!cancelled && result.ok) setStats(donorStats(result.value, colomboToday()));
      });
      return () => {
        cancelled = true;
      };
    }, [api]),
  );

  const next = stats?.next;

  return (
    <>
      <Card rim>
        <Text variant="voice">{t("App.profile.thanks")}</Text>
        <View style={styles.stats}>
          <Stat value={stats?.upcoming} label={t("App.profile.stats.upcoming")} />
          <View style={[styles.divider, { backgroundColor: palette.hairline }]} />
          <Stat value={stats?.given} label={t("App.profile.stats.given")} />
          <View style={[styles.divider, { backgroundColor: palette.hairline }]} />
          <Stat value={stats?.fed} label={t("App.profile.stats.fed")} />
        </View>
      </Card>

      {next && (
        <Pressable
          accessibilityRole="link"
          onPress={() => router.navigate("/bookings")}
          style={({ pressed }) => [
            styles.next,
            { backgroundColor: pressed ? palette.subtle : palette.surface, borderColor: palette.hairline },
          ]}
        >
          <View style={[styles.nextDay, { backgroundColor: palette.primary }]}>
            <Text variant="heading" style={{ color: palette.onPrimary }}>
              {Number(next.date.slice(8))}
            </Text>
            <Text variant="caption" style={{ color: palette.onPrimary }}>
              {dates.monthShort(next.date)}
            </Text>
          </View>
          <View style={styles.grow}>
            <Text variant="eyebrow">{t("App.profile.next")}</Text>
            <Text variant="label">
              {inLocale(language, next.slotName, next.slotNameSi, next.slotNameTa)} · {next.startTime}
            </Text>
            <Text variant="muted">{inLocale(language, next.institutionName, next.institutionNameSi, next.institutionNameTa)}</Text>
          </View>
          <Icon name="forward" size={18} color={palette.line} />
        </Pressable>
      )}
    </>
  );
}

function Stat({ value, label }: { value: number | undefined; label: string }) {
  const palette = usePalette();
  return (
    <View style={styles.stat} accessible accessibilityLabel={`${value ?? "–"} ${label}`}>
      <Text variant="display" style={{ color: palette.brand }}>
        {value ?? "–"}
      </Text>
      <Text variant="caption" style={styles.centred}>
        {label}
      </Text>
    </View>
  );
}

/** A visitor: what an account gives them, and the two ways in. */
function Welcome() {
  const { t } = useI18n();
  const rows: { icon: IconName; words: string }[] = [
    { icon: "search", words: t("App.profile.benefits.find") },
    { icon: "calendar", words: t("App.profile.benefits.book") },
    { icon: "language", words: t("App.profile.benefits.remind") },
  ];

  return (
    <>
      <View style={[styles.panel, { backgroundColor: brand.estate }]}>
        <View style={styles.grow}>
          <Text variant="title" accessibilityRole="header" style={styles.onPanel}>
            {t("App.profile.joinTitle")}
          </Text>
          <Text variant="voice" style={styles.onPanelSoft}>
            {t("App.profile.signedOut")}
          </Text>
        </View>
        <Bowl fill={0} color={brand.gold} scale={2} />
      </View>
      <Card>
        {rows.map((row) => (
          <View key={row.icon} style={styles.benefit}>
            <Tile icon={row.icon} />
            <Text style={styles.grow}>{row.words}</Text>
          </View>
        ))}
        <View style={styles.buttons}>
          <Button label={t("Login.submit")} onPress={() => router.push("/sign-in")} />
          <Button label={t("Login.createAccount")} variant="secondary" onPress={() => router.push("/sign-up")} />
        </View>
      </Card>
    </>
  );
}

function Preferences({ signedIn }: { signedIn: boolean }) {
  const { t, language, setLanguage } = useI18n();
  const { theme, setTheme } = useThemePreference();
  const client = useSessionClient();
  const palette = usePalette();

  /** Changes the app's language at once, and a signed-in account's, so SMS follow it too. */
  function chooseLanguage(next: Language) {
    if (next === language) return;
    setLanguage(next);
    if (!signedIn) return;
    void client
      .fetch("/api/me/language", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ language: next }),
      })
      .catch(() => {});
  }

  return (
    <View style={styles.group}>
      <SectionTitle>{t("App.profile.preferences")}</SectionTitle>
      <Card>
        <Row icon="language" label={t("LanguageSwitcher.label")}>
          <Segmented
            label={t("LanguageSwitcher.label")}
            options={LANGUAGES.map((code) => ({ value: code, label: t(`LanguageSwitcher.names.${code}`) }))}
            selected={language}
            onSelect={chooseLanguage}
          />
        </Row>
        <View style={[styles.rule, { backgroundColor: palette.hairline }]} />
        <Row icon="theme" label={t("App.profile.appearance")}>
          <Segmented<ThemeChoice>
            label={t("App.profile.appearance")}
            options={THEMES.map((value) => ({ value, label: t(`App.profile.theme.${value}`) }))}
            selected={theme}
            onSelect={setTheme}
          />
        </Row>
        {signedIn && <PushRow />}
      </Card>
    </View>
  );
}

/**
 * Push on this phone (F32). Hidden where push cannot work (Expo Go). Reads the phone again on coming
 * back, so a donor returning from Android's settings sees what they just changed.
 */
function PushRow() {
  const { t } = useI18n();
  const push = usePush();
  const channelName = useChannelName();
  const palette = usePalette();
  const [status, setStatus] = useState<PushStatus>("unsupported");
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);

  const read = useCallback(() => {
    void push.status().then(setStatus);
  }, [push]);

  useFocusEffect(read);

  useEffect(() => {
    const subscription = AppState.addEventListener("change", (state) => {
      if (state === "active") read();
    });
    return () => subscription.remove();
  }, [read]);

  async function change(on: boolean) {
    setBusy(true);
    setFailed(false);
    const outcome = await (on ? push.enable(channelName) : push.disable());
    if (outcome === "failed") setFailed(true);
    else setStatus(outcome);
    setBusy(false);
  }

  if (status === "unsupported") return null;

  return (
    <>
      <View style={[styles.rule, { backgroundColor: palette.hairline }]} />
      <View style={styles.row}>
        <View style={styles.rowHead}>
          <Tile icon="notifications" />
          <Text variant="label" style={styles.grow}>
            {t("App.profile.push.label")}
          </Text>
          <Switch
            accessibilityLabel={t("App.profile.push.label")}
            value={status === "on"}
            disabled={busy}
            onValueChange={(on) => void change(on)}
            trackColor={{ false: palette.hairline, true: palette.primary }}
          />
        </View>
        <Text variant="muted">{t(status === "blocked" ? "App.profile.push.blocked" : "App.profile.push.help")}</Text>
        {status === "blocked" && (
          <Button label={t("App.profile.push.openSettings")} variant="secondary" onPress={() => void Linking.openSettings()} />
        )}
        {failed && (
          <Text variant="muted" accessibilityLiveRegion="polite" style={{ color: palette.danger }}>
            {t("App.profile.push.failed")}
          </Text>
        )}
      </View>
    </>
  );
}

function SignOut() {
  const { t } = useI18n();
  const client = useSessionClient();
  const push = usePush();
  const [busy, setBusy] = useState<"logout" | "logout-all" | null>(null);

  async function end(kind: "logout" | "logout-all") {
    setBusy(kind);
    // First: the removal needs the access token that signing out ends (F31).
    await push.forget();
    // Signing out flips the session to anonymous; this screen then shows the ways in.
    await (kind === "logout" ? client.logout() : client.logoutAll());
    setBusy(null);
  }

  return (
    <View style={styles.group}>
      <SectionTitle>{t("App.profile.account")}</SectionTitle>
      <Card>
        <Button
          label={t("PasswordChange.title")}
          variant="secondary"
          icon="password"
          disabled={busy !== null}
          onPress={() => router.push("/change-password")}
        />
        <Button
          label={t("Session.signOut")}
          variant="secondary"
          icon="signOut"
          busy={busy === "logout"}
          disabled={busy !== null}
          onPress={() => void end("logout")}
        />
        <Text variant="muted">{t("App.profile.signOutEverywhereHelp")}</Text>
        <Button
          label={t("Session.signOutEverywhere")}
          variant="danger"
          icon="devices"
          busy={busy === "logout-all"}
          disabled={busy !== null}
          onPress={() => void end("logout-all")}
        />
      </Card>
    </View>
  );
}

function About() {
  const { t } = useI18n();
  return (
    <View style={styles.about}>
      <Image source={require("@/assets/images/splash-icon.png")} style={styles.mark} accessibilityIgnoresInvertColors />
      <Text variant="voice" style={styles.centred}>
        {t("Brand.tagline")}
      </Text>
      <Text variant="caption">
        {t("Brand.name")} · {t("App.profile.version", { version: Constants.expoConfig?.version ?? "" })}
      </Text>
    </View>
  );
}

/** A preference: its icon and name, then its control on the line below (labels run long in si/ta). */
function Row({ icon, label, children }: { icon: IconName; label: string; children: ReactNode }) {
  return (
    <View style={styles.row}>
      <View style={styles.rowHead}>
        <Tile icon={icon} />
        <Text variant="label" style={styles.grow}>
          {label}
        </Text>
      </View>
      {children}
    </View>
  );
}

function Tile({ icon }: { icon: IconName }) {
  const palette = usePalette();
  return (
    <View style={[styles.tile, { backgroundColor: palette.subtle }]}>
      <Icon name={icon} size={18} color={palette.primary} />
    </View>
  );
}

/** A row of choices, one chosen: a radio group, drawn as a segmented pill. */
function Segmented<T extends string>({
  label,
  options,
  selected,
  onSelect,
}: {
  label: string;
  options: { value: T; label: string }[];
  selected: T;
  onSelect: (value: T) => void;
}) {
  const palette = usePalette();
  return (
    <View
      style={[styles.segmented, { backgroundColor: palette.subtle, borderColor: palette.hairline }]}
      accessibilityRole="radiogroup"
      accessibilityLabel={label}
    >
      {options.map((option) => {
        const chosen = option.value === selected;
        return (
          <Pressable
            key={option.value}
            accessibilityRole="radio"
            accessibilityState={{ selected: chosen }}
            onPress={() => onSelect(option.value)}
            style={[styles.segment, chosen && [styles.segmentChosen, { backgroundColor: palette.surface, borderColor: palette.primary }]]}
          >
            <Text variant="label" style={[styles.centred, { color: chosen ? palette.primary : palette.fg }]}>
              {option.label}
            </Text>
          </Pressable>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  grow: { flex: 1 },
  centred: { textAlign: "center" },
  hero: { flexDirection: "row", alignItems: "center", gap: spacing.md, paddingTop: spacing.sm },
  heroText: { flex: 1, gap: 2 },
  avatarRing: { padding: 3, borderRadius: 44, borderWidth: 2 },
  avatar: { width: 72, height: 72, borderRadius: 36, alignItems: "center", justifyContent: "center" },
  acting: { flexDirection: "row", alignItems: "center", gap: spacing.sm, borderWidth: 1, borderRadius: radius.md, padding: spacing.md },
  stats: { flexDirection: "row", alignItems: "stretch", marginTop: spacing.xs },
  stat: { flex: 1, alignItems: "center", gap: 2, paddingVertical: spacing.xs },
  divider: { width: 1, marginVertical: spacing.xs },
  next: { ...bowl, flexDirection: "row", alignItems: "center", gap: spacing.md, borderWidth: 1, padding: spacing.md },
  nextDay: { width: 52, height: 56, borderRadius: radius.md, alignItems: "center", justifyContent: "center" },
  panel: { ...bowl, flexDirection: "row", alignItems: "flex-end", gap: spacing.md, padding: spacing.lg, marginTop: spacing.sm },
  onPanel: { color: brand.ivory },
  onPanelSoft: { color: brand.ivory, opacity: 0.85 },
  benefit: { flexDirection: "row", alignItems: "center", gap: spacing.sm + 4 },
  buttons: { gap: spacing.sm, marginTop: spacing.sm },
  group: { gap: spacing.sm },
  row: { gap: spacing.sm + 4, paddingVertical: spacing.xs },
  rowHead: { flexDirection: "row", alignItems: "center", gap: spacing.sm + 4 },
  rule: { height: 1, marginVertical: spacing.xs },
  tile: { width: 36, height: 36, borderRadius: 12, alignItems: "center", justifyContent: "center" },
  segmented: { flexDirection: "row", borderWidth: 1, borderRadius: radius.pill, padding: 4, gap: 4 },
  segment: { flex: 1, minHeight: 44, borderRadius: radius.pill, borderWidth: 1.5, borderColor: "transparent", justifyContent: "center", paddingHorizontal: spacing.xs },
  segmentChosen: { borderWidth: 1.5 },
  about: { alignItems: "center", gap: spacing.xs, paddingTop: spacing.lg },
  mark: { width: 40, height: 40, opacity: 0.9 },
});
