import { ActivityIndicator, Pressable, StyleSheet, View } from "react-native";

import { radius, spacing } from "@/theme/colors";
import { usePalette } from "@/theme/use-theme";

import { Icon, type IconName } from "./icon";
import { Text } from "./text";

type Props = {
  label: string;
  onPress: () => void;
  variant?: "primary" | "secondary" | "quiet" | "danger" | "link";
  icon?: IconName;
  busy?: boolean;
  disabled?: boolean;
};

/**
 * A full pill (DESIGN.md: never bowl a button). A press settles it to 97 % so a thumb feels it
 * land. `quiet` is a soft tinted pill for second actions inside a card; `danger` the outline
 * for something that ends a session or a booking. A busy button keeps its width.
 */
export function Button({ label, onPress, variant = "primary", icon, busy = false, disabled = false }: Props) {
  const palette = usePalette();
  const inactive = disabled || busy;

  if (variant === "link") {
    return (
      <Pressable
        accessibilityRole="button"
        onPress={onPress}
        disabled={inactive}
        hitSlop={8}
        style={({ pressed }) => [styles.link, { opacity: pressed || inactive ? 0.6 : 1 }]}
      >
        <Text variant="label" style={{ color: palette.primary }}>
          {label}
        </Text>
      </Pressable>
    );
  }

  const look = {
    primary: { rest: palette.primary, pressed: palette.primaryStrong, border: palette.primary, text: palette.onPrimary },
    secondary: { rest: palette.surface, pressed: palette.subtle, border: palette.line, text: palette.fg },
    quiet: { rest: palette.subtle, pressed: palette.hairline, border: palette.subtle, text: palette.primary },
    danger: { rest: palette.surface, pressed: palette.dangerSoft, border: palette.danger, text: palette.danger },
  }[variant];

  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ disabled: inactive, busy }}
      onPress={onPress}
      disabled={inactive}
      style={({ pressed }) => [
        styles.button,
        { backgroundColor: pressed ? look.pressed : look.rest, borderColor: look.border },
        pressed && styles.pressed,
        { opacity: disabled && !busy ? 0.5 : 1 },
      ]}
    >
      <View style={styles.content}>
        {busy ? (
          <ActivityIndicator color={look.text} />
        ) : (
          icon && <Icon name={icon} size={18} color={look.text} />
        )}
        <Text variant="label" style={[styles.label, { color: look.text }]}>
          {label}
        </Text>
      </View>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  button: {
    minHeight: 52,
    borderRadius: radius.pill,
    borderWidth: 1.5,
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.sm,
    justifyContent: "center",
  },
  pressed: { transform: [{ scale: 0.97 }] },
  content: { flexDirection: "row", gap: spacing.sm, justifyContent: "center", alignItems: "center" },
  label: { textAlign: "center", flexShrink: 1 },
  link: { minHeight: 44, justifyContent: "center", alignSelf: "flex-start" },
});
