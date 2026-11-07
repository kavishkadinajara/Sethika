import { SymbolView, type AndroidSymbol, type SFSymbol } from "expo-symbols";

import { usePalette } from "@/theme/use-theme";

/*  The app's few icons, by meaning: an SF Symbol on iOS, a Material Symbol on Android, both from
    expo-symbols, which the app already has (no icon library, as on the web). Icons are always
    decorative here: the words next to them, or the label of the control around them, say the
    same thing, so screen readers skip them.                                                  */

const ICONS = {
  search: { ios: "magnifyingglass", android: "search" },
  place: { ios: "mappin.and.ellipse", android: "location_on" },
  time: { ios: "clock", android: "schedule" },
  people: { ios: "person.2", android: "groups" },
  calendar: { ios: "calendar", android: "event" },
  meal: { ios: "fork.knife", android: "restaurant" },
  heart: { ios: "heart", android: "favorite" },
  give: { ios: "hands.sparkles", android: "volunteer_activism" },
  check: { ios: "checkmark.circle.fill", android: "check_circle" },
  forward: { ios: "chevron.right", android: "chevron_right" },
  back: { ios: "chevron.left", android: "chevron_left" },
  close: { ios: "xmark", android: "close" },
  plus: { ios: "plus", android: "add" },
  minus: { ios: "minus", android: "remove" },
  language: { ios: "character.bubble", android: "translate" },
  theme: { ios: "circle.lefthalf.filled", android: "contrast" },
  signOut: { ios: "rectangle.portrait.and.arrow.right", android: "logout" },
  devices: { ios: "laptopcomputer.and.iphone", android: "devices" },
  info: { ios: "info.circle", android: "info" },
  history: { ios: "clock.arrow.circlepath", android: "history" },
  map: { ios: "map", android: "map" },
  person: { ios: "person", android: "person" },
  notifications: { ios: "bell", android: "notifications" },
  password: { ios: "key", android: "key" },
  show: { ios: "eye", android: "visibility" },
  hide: { ios: "eye.slash", android: "visibility_off" },
} as const satisfies Record<string, { ios: SFSymbol; android: AndroidSymbol }>;

export type IconName = keyof typeof ICONS;

export function Icon({ name, size = 20, color }: { name: IconName; size?: number; color?: string }) {
  const palette = usePalette();
  return (
    <SymbolView
      name={ICONS[name]}
      size={size}
      tintColor={color ?? palette.muted}
      accessibilityElementsHidden
      importantForAccessibility="no-hide-descendants"
    />
  );
}
