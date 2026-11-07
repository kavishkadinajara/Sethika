import type { ReactNode } from "react";

import AppLayout from "@/app/(app)/_layout";
import { render } from "@/test-utils/render";

import { colors } from "./colors";

/*  The real tab layout, with the native bar swapped for one that keeps the props it was given:
    the bar itself is native code, so what it is told is the part a test can see.             */

let mockScheme: "light" | "dark" = "light";
let mockLanguage = "en";
let mockBar: Record<string, unknown> = {};
let mockInsidePush: ReactNode = null;

jest.mock("react-native/Libraries/Utilities/useColorScheme", () => ({ __esModule: true, default: () => mockScheme }));
jest.mock("@/i18n/i18n-provider", () => ({
  useI18n: () => ({ language: mockLanguage, t: (key: string) => key, setLanguage: () => {} }),
}));
jest.mock("@/auth/profile-provider", () => ({
  ProfileProvider: ({ children }: { children: ReactNode }) => children,
  useProfile: () => null,
}));
jest.mock("@/notifications/push-provider", () => ({
  PushProvider: ({ children }: { children: ReactNode }) => {
    mockInsidePush = children;
    return children;
  },
}));
jest.mock("expo-router/native-tabs", () => {
  const NativeTabs = ({ children, ...props }: { children: ReactNode }) => {
    mockBar = props;
    return children;
  };
  function Trigger() {
    return null;
  }
  Trigger.Label = function Label() {
    return null;
  };
  Trigger.Icon = function Icon() {
    return null;
  };
  NativeTabs.Trigger = Trigger;
  return { NativeTabs };
});

beforeEach(() => {
  mockScheme = "light";
  mockLanguage = "en";
  mockBar = {};
  mockInsidePush = null;
});

it("keeps the tabs inside push: Profile's switch and sign-out need it", async () => {
  await render(<AppLayout />);

  expect(mockInsidePush).not.toBeNull();
});

describe.each([
  ["dark", colors.dark, "#e0a33e"],
  ["light", colors.light, "#16513d"],
] as const)("the tab bar in %s", (scheme, palette, selected) => {
  beforeEach(async () => {
    mockScheme = scheme;
    await render(<AppLayout />);
  });

  it("stands on the surface, with no platform default left showing", () => {
    expect(mockBar).toMatchObject({
      backgroundColor: palette.surface,
      indicatorColor: palette.subtle,
      rippleColor: palette.subtle,
      shadowColor: palette.hairline,
      disableTransparentOnScrollEdge: true,
    });
  });

  it("marks the selected tab in the primary colour and the rest in muted", () => {
    expect(mockBar).toMatchObject({
      labelStyle: { default: { color: palette.muted }, selected: { color: selected } },
      iconColor: { default: palette.muted, selected },
    });
  });
});

describe("the tab labels' font", () => {
  it("is Outfit in English", async () => {
    await render(<AppLayout />);
    expect(mockBar).toMatchObject({
      labelStyle: { default: { fontFamily: "Outfit_500Medium" }, selected: { fontFamily: "Outfit_500Medium" } },
    });
  });

  it.each(["si", "ta"])("is the phone's own in %s, Outfit being Latin only", async (language) => {
    mockLanguage = language;
    await render(<AppLayout />);
    const { labelStyle } = mockBar as { labelStyle: { default: object; selected: object } };
    expect(labelStyle.default).not.toHaveProperty("fontFamily");
    expect(labelStyle.selected).not.toHaveProperty("fontFamily");
  });
});
