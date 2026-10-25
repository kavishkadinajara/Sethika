import { StyleSheet, View } from "react-native";

type Props = {
  /** How much of the bowl is filled, 0 to 1: the share of the day's places already taken. */
  fill: number;
  color: string;
  /** 1 is the calendar's size (28 x 18); the empty state uses a larger one. */
  scale?: number;
};

/**
 * Sethika's mark as a gauge (docs/DESIGN.md, the bowl), drawn with Views so the app needs no SVG
 * library: an empty bowl is a day waiting for a sponsor, and each sponsor fills it a little,
 * until it is full. Decorative: the day's words say the same thing to a screen reader.
 */
export function Bowl({ fill, color, scale = 1 }: Props) {
  const level = Math.min(1, Math.max(0, fill));
  const width = 28 * scale;
  const depth = 13 * scale;
  const stroke = 1.6 * scale;

  return (
    <View
      accessibilityElementsHidden
      importantForAccessibility="no-hide-descendants"
      style={[styles.bowl, { width, height: depth + 3 * scale }]}
    >
      <View
        style={{
          width,
          height: depth,
          borderWidth: stroke,
          borderColor: color,
          borderBottomLeftRadius: width / 2,
          borderBottomRightRadius: width / 2,
          overflow: "hidden",
          justifyContent: "flex-end",
        }}
      >
        {level > 0 && <View testID="bowl-fill" style={{ height: `${level * 100}%`, backgroundColor: color, opacity: 0.8 }} />}
      </View>
      <View style={{ width: 8 * scale, height: stroke, borderRadius: stroke, backgroundColor: color, marginTop: 1.4 * scale }} />
    </View>
  );
}

const styles = StyleSheet.create({
  bowl: { alignItems: "center" },
});
