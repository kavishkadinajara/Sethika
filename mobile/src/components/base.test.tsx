import { StyleSheet } from "react-native";

import { isDisabled, press, render } from "@/test-utils/render";
import { brand, colors } from "@/theme/colors";

import { Button } from "./button";
import { BrandHeader, Card, Screen } from "./screen";
import { Text } from "./text";
import { TextField } from "./text-field";

/*  The base every screen stands on (U.1), in both themes and in Latin and non-Latin scripts. */

let mockScheme: "light" | "dark" = "light";
let mockLanguage = "en";

jest.mock("react-native/Libraries/Utilities/useColorScheme", () => ({ __esModule: true, default: () => mockScheme }));
jest.mock("@/i18n/i18n-provider", () => ({
  useI18n: () => ({ language: mockLanguage, t: (key: string) => key, setLanguage: () => {} }),
}));

const flat = (style: unknown) => StyleSheet.flatten(style as never) as unknown as Record<string, unknown>;

beforeEach(() => {
  mockScheme = "light";
  mockLanguage = "en";
});

describe("Text", () => {
  it("keeps its own leading for Latin", async () => {
    const screen = await render(<Text>Words</Text>);
    expect(flat(screen.getText("Words").props.style).lineHeight).toBe(24);
  });

  it.each(["si", "ta"])("gives %s the web's 1.65 leading, so vowel signs clear the line above", async (language) => {
    mockLanguage = language;
    const screen = await render(
      <>
        <Text>Body</Text>
        <Text variant="date">Date</Text>
      </>,
    );
    expect(flat(screen.getText("Body").props.style).lineHeight).toBe(27);
    expect(flat(screen.getText("Date").props.style).lineHeight).toBe(73);
  });

  it("measures that leading from a size the caller set", async () => {
    mockLanguage = "si";
    const screen = await render(<Text style={{ fontSize: 20 }}>Sized</Text>);
    expect(flat(screen.getText("Sized").props.style).lineHeight).toBe(33);
  });

  it("lets a caller's own line height stand", async () => {
    mockLanguage = "si";
    const screen = await render(<Text style={{ lineHeight: 22 }}>Fixed</Text>);
    expect(flat(screen.getText("Fixed").props.style).lineHeight).toBe(22);
  });

  it("gives the weekday the date's voice, and the sentence the plain one", async () => {
    mockScheme = "dark";
    const screen = await render(
      <>
        <Text variant="weekday">Tuesday</Text>
        <Text variant="sentence">Next is lunch.</Text>
      </>,
    );
    const weekday = flat(screen.getText("Tuesday").props.style);
    const sentence = flat(screen.getText("Next is lunch.").props.style);
    expect(weekday).toMatchObject({ fontFamily: "Fraunces_400Regular_Italic", color: colors.dark.brand, fontSize: 24, lineHeight: 32 });
    expect(sentence).toMatchObject({ fontFamily: "Outfit_400Regular", color: colors.dark.fg, fontSize: 19, lineHeight: 27 });
  });

  it("does not fake a slanted Sinhala weekday, and leaves it room", async () => {
    mockLanguage = "si";
    const screen = await render(<Text variant="weekday">අඟහරුවාදා</Text>);
    const style = flat(screen.getText("අඟහරුවාදා").props.style);
    expect(style.fontFamily).toBeUndefined();
    expect(style.lineHeight).toBe(40);
  });

  it("says the date in Fraunces italic and the brand colour", async () => {
    mockScheme = "dark";
    const screen = await render(<Text variant="date">6 November</Text>);
    const style = flat(screen.getText("6 November").props.style);
    expect(style.fontFamily).toBe("Fraunces_400Regular_Italic");
    expect(style.color).toBe(colors.dark.brand);
  });

  it("says the date upright in Sinhala, Fraunces being Latin only", async () => {
    mockLanguage = "si";
    const screen = await render(<Text variant="date">නොවැම්බර්</Text>);
    expect(flat(screen.getText("නොවැම්බර්").props.style).fontFamily).toBeUndefined();
  });
});

describe("Button", () => {
  const spinners = (screen: Awaited<ReturnType<typeof render>>) => screen.where((props) => props.color === colors.light.onPrimary);

  it("keeps the label in place under the spinner while busy", async () => {
    const screen = await render(<Button label="Book" busy onPress={() => {}} />);
    expect(screen.getText("Book")).toBeTruthy();
    expect(spinners(screen).length).toBeGreaterThan(0);
    const content = screen.getText("Book").parent;
    expect(flat(content?.props.style).opacity).toBe(0);
  });

  it("shows no spinner at rest, and the label can be seen", async () => {
    const screen = await render(<Button label="Book" onPress={() => {}} />);
    expect(spinners(screen)).toHaveLength(0);
    expect(flat(screen.getText("Book").parent?.props.style).opacity).toBeUndefined();
  });

  it("ignores a tap while busy", async () => {
    const onPress = jest.fn();
    const screen = await render(<Button label="Book" busy onPress={onPress} />);
    await press(screen.getText("Book"));
    expect(onPress).not.toHaveBeenCalled();
    expect(isDisabled(screen.getText("Book"))).toBe(true);
  });

  it("fades a disabled button to the web's 55 %, and not a busy one", async () => {
    const screen = await render(
      <>
        <Button label="Off" disabled onPress={() => {}} />
        <Button label="Working" busy onPress={() => {}} />
      </>,
    );
    const button = (label: string) => screen.where((props) => props.accessibilityRole === "button").find((node) => node.queryAll((inner) => inner.children.includes(label)).length > 0);
    expect(flat(button("Off")?.props.style).opacity).toBe(0.55);
    expect(flat(button("Working")?.props.style).opacity).toBe(1);
  });

  it("is gold with near-black words in dark, green with ivory words in light", async () => {
    mockScheme = "dark";
    const dark = await render(<Button label="Book" onPress={() => {}} />);
    const [gold] = dark.where((props) => props.accessibilityRole === "button");
    expect(flat(gold.props.style).backgroundColor).toBe("#e0a33e");
    expect(flat(dark.getText("Book").props.style).color).toBe("#0a0a0a");

    mockScheme = "light";
    const light = await render(<Button label="Book" onPress={() => {}} />);
    const [green] = light.where((props) => props.accessibilityRole === "button");
    expect(flat(green.props.style).backgroundColor).toBe("#16513d");
    expect(flat(light.getText("Book").props.style).color).toBe("#f7f4ec");
  });

  it("raises only the filled primary", async () => {
    const screen = await render(
      <>
        <Button label="Main" onPress={() => {}} />
        <Button label="Other" variant="secondary" onPress={() => {}} />
      </>,
    );
    const [main, other] = screen.where((props) => props.accessibilityRole === "button");
    expect(flat(main.props.style).boxShadow).toBeDefined();
    expect(flat(other.props.style).boxShadow).toBeUndefined();
  });
});

describe("Card", () => {
  const card = async () => {
    const screen = await render(<Card style={{ margin: 1 }}><Text>Inside</Text></Card>);
    const [view] = screen.where((props) => flat(props.style)?.margin === 1);
    return flat(view.props.style);
  };

  it("has the black depth and a light top edge in dark", async () => {
    mockScheme = "dark";
    const style = await card();
    expect(style.backgroundColor).toBe("#0c0c0c");
    expect(style.shadowColor).toBeUndefined();
    expect(style.elevation).toBeUndefined();
    expect(style.boxShadow).toEqual(expect.arrayContaining([expect.objectContaining({ inset: true, offsetY: 1 })]));
  });

  it("has the green-tinted depth in light", async () => {
    const style = await card();
    expect(style.shadowColor).toBe(brand.ink);
    expect(style.boxShadow).toBeUndefined();
  });

  it("draws the rim short, as on the web, and only when asked", async () => {
    const rimmed = (props: Record<string, unknown>) => flat(props.style)?.height === 3;
    const plain = await render(<Card><Text>Plain</Text></Card>);
    expect(plain.where(rimmed)).toHaveLength(0);

    const screen = await render(<Card rim><Text>Rim</Text></Card>);
    const [rim] = screen.where(rimmed);
    expect(flat(rim.props.style)).toMatchObject({ left: 20, width: 48, backgroundColor: colors.light.accent });
    expect(flat(rim.props.style).right).toBeUndefined();
  });
});

describe("Screen", () => {
  it("lays the page on the true-black canvas in dark", async () => {
    mockScheme = "dark";
    const screen = await render(<Screen><Text>Page</Text></Screen>);
    expect(screen.where((props) => flat(props.style)?.backgroundColor === "#000000").length).toBeGreaterThan(0);
  });

  it("keeps Android's pull-to-refresh disc off white in dark", async () => {
    mockScheme = "dark";
    const screen = await render(<Screen onRefresh={() => {}}><Text>Page</Text></Screen>);
    // React Native's Jest mock draws the control without its props, so read what the list was given.
    const [list] = screen.where((props) => "refreshControl" in props);
    const control = (list.props.refreshControl as { props: Record<string, unknown> }).props;
    expect(control.progressBackgroundColor).toBe(colors.dark.surface);
    expect(control.tintColor).toBe(colors.dark.primary);
  });

  it("shows the ivory mark on black and the green mark on ivory, with no tile behind it", async () => {
    const mark = async () => {
      const screen = await render(<BrandHeader />);
      const [image] = screen.where((props) => "source" in props);
      return JSON.stringify(image.props.source);
    };
    expect(await mark()).not.toContain("dark");
    mockScheme = "dark";
    expect(await mark()).toContain("splash-icon-dark");
  });
});

describe("a password field", () => {
  const box = (screen: Awaited<ReturnType<typeof render>>) => screen.getLabel("Password");

  it("starts hidden, and the eye shows it and hides it again, saying which it will do", async () => {
    const screen = await render(<TextField label="Password" password />);
    expect(box(screen).props.secureTextEntry).toBe(true);

    await press(screen.getLabel("Common.showPassword"));
    expect(box(screen).props.secureTextEntry).toBe(false);

    await press(screen.getLabel("Common.hidePassword"));
    expect(box(screen).props.secureTextEntry).toBe(true);
  });

  it("gives the eye a full target, and no words to run out of the box", async () => {
    const screen = await render(<TextField label="Password" password />);
    const eye = flat(screen.getLabel("Common.showPassword").props.style);
    expect(eye.width).toBeGreaterThanOrEqual(44);
    expect(eye.minHeight).toBeGreaterThanOrEqual(44);
    expect(screen.queryText("Common.showPassword")).toBeNull();
  });

  it("has no eye on an ordinary field", async () => {
    const screen = await render(<TextField label="Name" />);
    expect(screen.queryLabel("Common.showPassword")).toBeNull();
  });
});
