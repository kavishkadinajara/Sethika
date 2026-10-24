import { useState } from "react";
import { Pressable, StyleSheet, TextInput, View, type TextInputProps } from "react-native";

import { useI18n } from "@/i18n/i18n-provider";
import { radius, spacing } from "@/theme/colors";
import { usePalette } from "@/theme/use-theme";

import { Text } from "./text";

type Props = Omit<TextInputProps, "style"> & {
  label: string;
  help?: string;
  error?: string | null;
  /** A password: hidden, with a show/hide control. */
  password?: boolean;
};

export function TextField({ label, help, error, password = false, ...input }: Props) {
  const palette = usePalette();
  const { t } = useI18n();
  const [hidden, setHidden] = useState(true);

  return (
    <View style={styles.field}>
      <Text variant="label">{label}</Text>
      {help && <Text variant="muted">{help}</Text>}
      <View
        style={[styles.box, { backgroundColor: palette.surface, borderColor: error ? palette.danger : palette.line }]}
      >
        <TextInput
          {...input}
          accessibilityLabel={label}
          secureTextEntry={password && hidden}
          autoCapitalize={password ? "none" : input.autoCapitalize}
          autoCorrect={password ? false : input.autoCorrect}
          placeholderTextColor={palette.muted}
          style={[styles.input, { color: palette.fg }]}
        />
        {password && (
          <Pressable
            accessibilityRole="button"
            onPress={() => setHidden((value) => !value)}
            hitSlop={8}
            style={styles.toggle}
          >
            <Text variant="muted" style={{ color: palette.primary }}>
              {hidden ? t("Common.showPassword") : t("Common.hidePassword")}
            </Text>
          </Pressable>
        )}
      </View>
      {error && (
        <Text variant="error" accessibilityLiveRegion="polite">
          {error}
        </Text>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  field: { gap: spacing.xs },
  box: {
    flexDirection: "row",
    alignItems: "center",
    borderWidth: 1,
    borderRadius: radius.sm,
    minHeight: 48,
  },
  input: { flex: 1, fontSize: 16, paddingHorizontal: spacing.md, paddingVertical: spacing.sm },
  toggle: { paddingHorizontal: spacing.md },
});
