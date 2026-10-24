import { ActivityIndicator, Pressable, StyleSheet, View } from "react-native";

import { radius, spacing } from "@/theme/colors";
import { usePalette } from "@/theme/use-theme";

import { Text } from "./text";

type Props = {
  label: string;
  onPress: () => void;
  variant?: "primary" | "secondary" | "link";
  busy?: boolean;
  disabled?: boolean;
};

export function Button({ label, onPress, variant = "primary", busy = false, disabled = false }: Props) {
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

  const primary = variant === "primary";
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ disabled: inactive, busy }}
      onPress={onPress}
      disabled={inactive}
      style={({ pressed }) => [
        styles.button,
        primary
          ? { backgroundColor: pressed ? palette.primaryStrong : palette.primary }
          : { backgroundColor: pressed ? palette.subtle : palette.surface, borderColor: palette.line, borderWidth: 1 },
        { opacity: disabled && !busy ? 0.5 : 1 },
      ]}
    >
      <View style={styles.content}>
        {busy && <ActivityIndicator color={primary ? palette.onPrimary : palette.primary} />}
        <Text variant="label" style={{ color: primary ? palette.onPrimary : palette.fg }}>
          {label}
        </Text>
      </View>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  button: {
    minHeight: 48,
    borderRadius: radius.md,
    paddingHorizontal: spacing.md,
    justifyContent: "center",
  },
  content: { flexDirection: "row", gap: spacing.sm, justifyContent: "center", alignItems: "center" },
  link: { minHeight: 44, justifyContent: "center", alignSelf: "flex-start" },
});
