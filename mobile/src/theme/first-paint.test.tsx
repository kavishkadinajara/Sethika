import * as SecureStore from "expo-secure-store";
import * as SplashScreen from "expo-splash-screen";
import * as SystemUI from "expo-system-ui";
import { Appearance } from "react-native";

import RootLayout from "@/app/_layout";
import { render, settle } from "@/test-utils/render";

import { ThemePreferenceProvider } from "./appearance";
import { colors } from "./colors";

/*  From the splash to the first screen: the root view is the theme's canvas, and the splash waits
    for a saved theme, so a dark choice on a light phone never paints light first.            */

let mockScheme: "light" | "dark" = "light";

jest.mock("react-native/Libraries/Utilities/useColorScheme", () => ({ __esModule: true, default: () => mockScheme }));
jest.mock("expo-system-ui", () => ({ setBackgroundColorAsync: jest.fn(() => Promise.resolve()) }));
jest.mock("expo-secure-store", () => ({ getItemAsync: jest.fn(), setItemAsync: jest.fn(() => Promise.resolve()) }));
jest.mock("expo-splash-screen", () => ({ preventAutoHideAsync: jest.fn(), hideAsync: jest.fn(() => Promise.resolve()) }));
jest.mock("expo-status-bar", () => ({ StatusBar: () => null }));
jest.mock("expo-router", () => {
  function Stack() {
    return null;
  }
  Stack.Screen = function Screen() {
    return null;
  };
  return {
    Stack,
    ThemeProvider: ({ children }: { children: unknown }) => children,
    DarkTheme: { colors: {} },
    DefaultTheme: { colors: {} },
  };
});
jest.mock("@expo-google-fonts/outfit", () => ({ useFonts: () => [true, null] }));
jest.mock("@/auth/session-provider", () => ({
  SessionProvider: ({ children }: { children: unknown }) => children,
  useSession: () => ({ status: "anonymous" }),
  useSessionClient: () => ({ retry: () => Promise.resolve() }),
}));

const stored = jest.mocked(SecureStore.getItemAsync);
const rootColour = jest.mocked(SystemUI.setBackgroundColorAsync);
const hideSplash = jest.mocked(SplashScreen.hideAsync);

beforeEach(() => {
  mockScheme = "light";
  stored.mockReset().mockResolvedValue(null);
  rootColour.mockClear();
  hideSplash.mockClear();
});

describe("the root view", () => {
  it("is black in dark", async () => {
    mockScheme = "dark";
    await render(<ThemePreferenceProvider>{null}</ThemePreferenceProvider>);
    expect(rootColour).toHaveBeenLastCalledWith(colors.dark.canvas);
    expect(colors.dark.canvas).toBe("#000000");
  });

  it("is Rice Ivory in light", async () => {
    await render(<ThemePreferenceProvider>{null}</ThemePreferenceProvider>);
    expect(rootColour).toHaveBeenLastCalledWith(colors.light.canvas);
  });

  it("follows the theme when it changes", async () => {
    const screen = await render(<ThemePreferenceProvider>{null}</ThemePreferenceProvider>);
    mockScheme = "dark";
    await screen.rerender(<ThemePreferenceProvider>{null}</ThemePreferenceProvider>);
    expect(rootColour).toHaveBeenLastCalledWith(colors.dark.canvas);
  });
});

describe("the splash", () => {
  it("stays up until the saved theme has been read and applied", async () => {
    const setColorScheme = jest.spyOn(Appearance, "setColorScheme").mockImplementation(() => {});
    let finish: (value: string) => void = () => {};
    stored.mockReturnValue(new Promise((resolve) => (finish = resolve)));

    await render(<RootLayout />);
    expect(hideSplash).not.toHaveBeenCalled();

    finish("dark");
    await settle();
    expect(setColorScheme).toHaveBeenCalledWith("dark");
    expect(hideSplash).toHaveBeenCalled();
    expect(setColorScheme.mock.invocationCallOrder[0]).toBeLessThan(hideSplash.mock.invocationCallOrder[0]);
    setColorScheme.mockRestore();
  });

  it("still goes when the saved theme cannot be read", async () => {
    stored.mockRejectedValue(new Error("Keystore unavailable"));
    await render(<RootLayout />);
    expect(hideSplash).toHaveBeenCalled();
  });
});
