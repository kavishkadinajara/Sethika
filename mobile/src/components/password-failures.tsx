import { View } from "react-native";

import { useI18n } from "@/i18n/i18n-provider";
import { hasMessage } from "@/i18n/translate";

import { Text } from "./text";

/** The rules a refused password broke (ApiErrors.PasswordRejected), each in words. */
export function PasswordFailures({ failures }: { failures: string[] }) {
  const { t } = useI18n();
  if (failures.length === 0) return null;

  return (
    <View accessibilityLiveRegion="polite">
      <Text variant="label">{t("Password.title")}</Text>
      {failures.map((failure) => {
        const key = `Password.${failure}`;
        return hasMessage(key) ? (
          <Text key={failure} variant="error">
            {"• "}
            {t(key)}
          </Text>
        ) : null;
      })}
    </View>
  );
}
