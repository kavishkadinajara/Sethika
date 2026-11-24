import appConfig from "../../app.json";
import { brand, colors, lifts } from "./colors";

/*  F19: the app's tokens are the web's. The file once said it matched and silently stopped
    matching for a week, so this reads web/app/globals.css itself: both themes, every token, in
    both directions. A token added on one side only fails here.                              */

// The app has no Node types, and one test is no reason to add them to every screen's globals.
declare const __dirname: string;
const { readFileSync } = jest.requireActual<{ readFileSync: (path: string, encoding: "utf8") => string }>("fs");

const css = readFileSync(`${__dirname}/../../../web/app/globals.css`, "utf8");

/** The text between `{` and its matching `}`, for the block that starts at `header`. */
function block(header: string): string {
  const start = css.indexOf(header);
  if (start < 0) throw new Error(`globals.css has no block ${header}`);
  const open = css.indexOf("{", start + header.length - 1);
  let depth = 0;
  for (let at = open; at < css.length; at++) {
    if (css[at] === "{") depth++;
    if (css[at] === "}" && --depth === 0) return css.slice(open + 1, at);
  }
  throw new Error(`Unclosed block ${header}`);
}

/** `rgb(14 42 33 / 0.45)`, `rgba(14, 42, 33, 0.45)` and `#ABC123` each reduce to one spelling. */
function normal(value: string): string {
  const rgb = /^rgba?\(\s*(\d+)[\s,]+(\d+)[\s,]+(\d+)\s*[/,]\s*([\d.]+)\s*\)$/.exec(value.trim());
  return rgb ? `rgba(${rgb[1]},${rgb[2]},${rgb[3]},${Number(rgb[4])})` : value.trim().toLowerCase();
}

const camel = (token: string) => token.replace(/-(\w)/g, (_, letter: string) => letter.toUpperCase());

/** Every `--color-*` in a block, keyed the way the app names it. */
function tokens(text: string): Record<string, string> {
  const found: Record<string, string> = {};
  for (const [, name, value] of text.matchAll(/--color-([a-z-]+):\s*([^;]+);/g)) found[camel(name)] = normal(value);
  return found;
}

function normalised(palette: Record<string, string>): Record<string, string> {
  return Object.fromEntries(Object.entries(palette).map(([name, value]) => [name, normal(value)]));
}

const { estate, gold, ink, ivory, ...webLight } = tokens(block("@theme {"));
const webDark = tokens(block(':root[data-theme="dark"] {'));
const webDarkByOs = tokens(block(':root:not([data-theme="light"]) {'));

describe("the app's colours are the web's (F19)", () => {
  it("reads the whole list from the web, so the checks below cannot pass on nothing", () => {
    expect(Object.keys(webLight)).toHaveLength(21);
    expect(Object.keys(webDark)).toHaveLength(21);
    expect(Object.keys(webDarkByOs)).toHaveLength(21);
  });

  it("matches the web's light theme, token for token", () => {
    expect(normalised(colors.light)).toEqual(webLight);
  });

  it("matches the web's dark theme, token for token", () => {
    expect(normalised(colors.dark)).toEqual(webDark);
  });

  it("matches the web's dark theme for a browser that follows the phone, too", () => {
    expect(normalised(colors.dark)).toEqual(webDarkByOs);
  });

  it("has the same brand colours", () => {
    expect(normalised(brand)).toEqual({ estate, gold, ink, ivory });
  });
});

describe("the splash", () => {
  const entry = appConfig.expo.plugins.find((plugin) => Array.isArray(plugin) && plugin[0] === "expo-splash-screen");
  const splash = (entry as unknown[])[1] as { backgroundColor: string; dark: { backgroundColor: string } };

  it("opens on the canvas of each theme, so the first screen does not change colour", () => {
    expect(splash.backgroundColor.toLowerCase()).toBe(colors.light.canvas);
    expect(splash.dark.backgroundColor.toLowerCase()).toBe(colors.dark.canvas);
  });
});

describe("warm depth (F20)", () => {
  it("is the web's dark lift: a light top edge and black shadows", () => {
    const web = /--shadow-lift:\s*([^;]+);/.exec(block(':root[data-theme="dark"] {'))?.[1] ?? "";
    const [edge, near, far] = web.split(/,\s*(?=\d)/).map((part) => part.replace(/\s+/g, " ").trim());
    const [appEdge, appNear, appFar] = lifts.dark.boxShadow;

    expect(appEdge.inset).toBe(true);
    expect(edge).toBe(`inset 0 ${appEdge.offsetY}px ${appEdge.blurRadius} rgb(255 255 255 / 0.07)`);
    expect(normal(appEdge.color)).toBe("rgba(255,255,255,0.07)");
    expect(near).toBe(`0 ${appNear.offsetY}px ${appNear.blurRadius}px rgb(0 0 0 / 0.6)`);
    expect(normal(appNear.color)).toBe("rgba(0,0,0,0.6)");
    expect(far).toBe(`0 ${appFar.offsetY}px ${appFar.blurRadius}px ${appFar.spreadDistance}px rgb(0 0 0 / 0.9)`);
    expect(normal(appFar.color)).toBe("rgba(0,0,0,0.9)");
  });

  it("keeps the green-tinted shadow in light, and none of it in dark", () => {
    expect(lifts.light.shadowColor).toBe(brand.ink);
    expect(lifts.dark).not.toHaveProperty("shadowColor");
    expect(lifts.dark).not.toHaveProperty("elevation");
  });
});
