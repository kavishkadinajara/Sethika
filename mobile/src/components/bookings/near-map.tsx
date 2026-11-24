import Constants from "expo-constants";
import { router } from "expo-router";
import { useMemo, useState } from "react";
import { Linking, Modal, StyleSheet, View } from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";
import { WebView, type WebViewMessageEvent } from "react-native-webview";
import type { ShouldStartLoadRequest } from "react-native-webview/lib/WebViewTypes";

import { MAP_BASE_URL, mapNavigation, mapPage, mapPins, mapUserAgent, readMapMessage } from "@/bookings/map-page";
import type { NearPoint } from "@/bookings/near-me";
import type { SearchPlace } from "@/bookings/types";
import { Button } from "@/components/button";
import { Text } from "@/components/text";
import { useI18n } from "@/i18n/i18n-provider";
import { spacing } from "@/theme/colors";
import { usePalette } from "@/theme/use-theme";

import { Loading } from "./states";

type Props = { places: SearchPlace[]; point: NearPoint; radiusKm: number; onClose: () => void };

/**
 * The near-me results on a map (F34, F70), over the list that opened it: a modal, so the point stays
 * in the list's memory (F59) and closing it finds the list as it was. A pin's name opens the place's
 * page, as its card does. The list is the accessible way to the same places, and the map says so.
 */
export function NearMap({ places, point, radiusKm, onClose }: Props) {
  const { t, language } = useI18n();
  const palette = usePalette();
  const [ready, setReady] = useState(false);
  const [broken, setBroken] = useState(false);
  const [gone, setGone] = useState(false);
  const label = t("App.near.mapTitle");
  const pins = useMemo(() => mapPins(places, language), [places, language]);
  const html = useMemo(
    () => mapPage({ language, palette, label, point, radiusKm, pins }),
    [language, palette, label, point, radiusKm, pins],
  );

  function heard(event: WebViewMessageEvent) {
    const message = readMapMessage(event.nativeEvent.data, pins);
    if (message?.type === "ready") setReady(true);
    if (message?.type === "tiles") setBroken(true);
    if (message?.type === "open") {
      onClose();
      router.push({ pathname: "/i/[slug]", params: { slug: message.slug } });
    }
  }

  function navigate({ url }: ShouldStartLoadRequest) {
    const decision = mapNavigation(url);
    if (decision === "browser") void Linking.openURL(url);
    return decision === "load";
  }

  function lost() {
    setGone(true);
    setBroken(true);
  }

  return (
    <Modal visible animationType="slide" onRequestClose={onClose}>
      <SafeAreaView edges={["top", "bottom"]} style={[styles.fill, { backgroundColor: palette.canvas }]}>
        <View style={styles.header}>
          <View style={styles.fill}>
            <Text variant="label" accessibilityRole="header">
              {label}
            </Text>
            <Text variant="caption" accessibilityLiveRegion="polite">
              {t(broken ? "App.near.mapFailed" : "App.near.mapHint")}
            </Text>
          </View>
          <Button label={t("Common.close")} variant="quiet" onPress={onClose} />
        </View>
        <View style={[styles.fill, { backgroundColor: palette.subtle }]}>
          {!gone && (
            <WebView
              testID="near-map"
              accessibilityLabel={label}
              source={{ html, baseUrl: MAP_BASE_URL }}
              userAgent={mapUserAgent(Constants.expoConfig?.version)}
              setSupportMultipleWindows={false}
              onMessage={heard}
              onShouldStartLoadWithRequest={navigate}
              onError={lost}
              onRenderProcessGone={lost}
              onContentProcessDidTerminate={lost}
              style={[styles.fill, { backgroundColor: palette.subtle }]}
            />
          )}
          {!ready && !gone && (
            <View style={styles.cover}>
              <Loading />
            </View>
          )}
        </View>
      </SafeAreaView>
    </Modal>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  header: { flexDirection: "row", alignItems: "flex-start", gap: spacing.sm, paddingHorizontal: spacing.md, paddingVertical: spacing.sm },
  cover: { ...StyleSheet.absoluteFill, justifyContent: "center" },
});
