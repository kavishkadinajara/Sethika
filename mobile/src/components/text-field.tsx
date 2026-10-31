import { useState } from "react";
import { Pressable, StyleSheet, TextInput, View, type TextInputProps } from "react-native";

import { useI18n } from "@/i18n/i18n-provider";
import { cup, spacing } from "@/theme/colors";
import { usePalette } from "@/theme/use-theme";

import { Icon, type IconName } from "./icon";
import { Text } from "./text";

type Props = Omit<TextInputProps, "style"> & {
  label: string;
  help?: string;
  error?: string | null;
  /** A password: hidden, with a show/hide control. */
  password?: boolean;
  /** A decorative icon at the start of the box. */
  icon?: IconName;
  /** Keeps the label for screen readers but does not show it (a search box with a placeholder). */
  hideLabel?: boolean;
};

/**
 * A cup-shaped box (DESIGN.md: inputs are cups, "put something in here"). Focus turns its edge
 * Estate Green.
 */
export function TextField({ label, help, error, password = false, icon, hideLabel = false, ...input }: Props) {
  const palette = usePalette();
  const { t } = useI18n();
  const [hidden, setHidden] = useState(true);
  const [focused, setFocused] = useState(false);

  return (
    <View style={styles.field}>
      {!hideLabel && <Text variant="label">{label}</Text>}
      {help && <Text variant="muted">{help}</Text>}
      {/* Focus changes only the border's colour. Changing the box's width or shadow while the
          field is focused made Android drop the focus, and the keyboard with it. */}
      <View
        collapsable={false}
        style={[
          styles.box,
          { backgroundColor: palette.surface, borderColor: error ? palette.danger : focused ? palette.primary : palette.line },
        ]}
      >
        {icon && (
          <View style={styles.icon}>
            <Icon name={icon} size={20} color={focused ? palette.primary : palette.muted} />
          </View>
        )}
        <TextInput
          {...input}
          onFocus={(event) => {
            setFocused(true);
            input.onFocus?.(event);
          }}
          onBlur={(event) => {
            setFocused(false);
            input.onBlur?.(event);
          }}
          accessibilityLabel={label}
          secureTextEntry={password && hidden}
          autoCapitalize={password ? "none" : input.autoCapitalize}
          autoCorrect={password ? false : input.autoCorrect}
          placeholderTextColor={palette.muted}
          style={[styles.input, icon && styles.inputWithIcon, { color: palette.fg }]}
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
    ...cup,
    borderWidth: 1.5,
    flexDirection: "row",
    alignItems: "center",
    minHeight: 52,
  },
  icon: { paddingLeft: spacing.md },
  input: { flex: 1, fontSize: 16, paddingHorizontal: spacing.md, paddingVertical: spacing.sm + 2 },
  inputWithIcon: { paddingLeft: spacing.sm },
  toggle: { paddingHorizontal: spacing.md },
});
