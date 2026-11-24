import { colors, type Palette } from "@/theme/colors";
import { searchPlace } from "@/test-utils/fixtures";

import { LEAFLET_CSS, LEAFLET_JS, LEAFLET_VERSION } from "./leaflet-bundle";
import { MAP_BASE_URL, MAP_SCRIPT, mapNavigation, mapPage, mapPins, mapUserAgent, OSM_COPYRIGHT, readMapMessage, type MapPin } from "./map-page";

// The app has no Node types, as in colors.test.ts.
declare const __dirname: string;
const { readFileSync } = jest.requireActual<{ readFileSync: (path: string, encoding: "utf8") => string }>("fs");
const read = (path: string) => JSON.parse(readFileSync(`${__dirname}/${path}`, "utf8"));

const pin = (overrides: Partial<MapPin> = {}): MapPin => ({ number: 1, slug: "home", name: "The Home", latitude: 6.9, longitude: 79.85, ...overrides });
const POINT = { latitude: 6.927, longitude: 79.862 };
const page = (pins: MapPin[] = [pin()], language: "en" | "si" | "ta" = "en", palette: Palette = colors.light) =>
  mapPage({ language, palette, label: "Map of the places", point: POINT, radiusKm: 25, pins });

describe("what OSM's tile policy asks (F69)", () => {
  it("names the app and its version, and does not pose as a browser", () => {
    expect(mapUserAgent("1.2.3")).toBe("Sethika/1.2.3 (+https://sethika.com)");
  });

  it("still names the app when the version is not known", () => {
    expect(mapUserAgent(null)).toBe("Sethika/0 (+https://sethika.com)");
    expect(mapUserAgent(undefined)).toBe("Sethika/0 (+https://sethika.com)");
    expect(mapUserAgent("")).toBe("Sethika/0 (+https://sethika.com)");
  });

  it("gives the page a real https address, so its tile requests carry a Referer", () => {
    expect(MAP_BASE_URL).toBe("https://sethika.com/");
  });

  it("uses exactly the tile address the policy gives", () => {
    expect(pageData(page()).data.tiles).toBe("https://tile.openstreetmap.org/{z}/{x}/{y}.png");
  });

  it("does not set a referrer policy that would strip the Referer", () => {
    expect(page().replace(LEAFLET_JS, "")).not.toMatch(/referrer/i);
  });

  it("credits OpenStreetMap with a link to its licence", () => {
    expect(pageData(page()).data.attribution).toBe('&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors');
  });
});

describe("where the page may go", () => {
  it.each([
    [MAP_BASE_URL, "load"],
    ["about:blank", "load"],
    [OSM_COPYRIGHT, "browser"],
    ["https://www.openstreetmap.org/copyright?x=1", "block"],
    ["http://www.openstreetmap.org/copyright", "block"],
    ["https://sethika.com/evil", "block"],
    ["https://evil.example/", "block"],
    ["sethika://i/home", "block"],
    ["javascript:alert(1)", "block"],
  ])("%s: %s", (url, decision) => {
    expect(mapNavigation(url)).toBe(decision);
  });
});

describe("pins", () => {
  it("numbers the places in the list's order, in the donor's language", () => {
    const places = [
      searchPlace({ slug: "a", name: "Near Home", nameSi: "ළඟ නිවාසය", latitude: 6.9, longitude: 79.8 }),
      searchPlace({ slug: "b", name: "Far Home", nameSi: "ඈත නිවාසය", latitude: 7.1, longitude: 80.1 }),
    ];

    expect(mapPins(places, "si")).toEqual([
      { number: 1, slug: "a", name: "ළඟ නිවාසය", latitude: 6.9, longitude: 79.8 },
      { number: 2, slug: "b", name: "ඈත නිවාසය", latitude: 7.1, longitude: 80.1 },
    ]);
  });
});

describe("what the page says", () => {
  const pins = [pin({ slug: "home" })];

  it("reads ready and tiles", () => {
    expect(readMapMessage('{"type":"ready"}', pins)).toEqual({ type: "ready" });
    expect(readMapMessage('{"type":"tiles"}', pins)).toEqual({ type: "tiles" });
  });

  it("opens only a place the app put on the map", () => {
    expect(readMapMessage('{"type":"open","slug":"home"}', pins)).toEqual({ type: "open", slug: "home" });
    expect(readMapMessage('{"type":"open","slug":"elsewhere"}', pins)).toBeNull();
    expect(readMapMessage('{"type":"open"}', pins)).toBeNull();
  });

  it.each(["not json", "null", "7", '{"type":"other"}', "{}"])("ignores %s", (data) => {
    expect(readMapMessage(data, pins)).toBeNull();
  });
});

describe("the page", () => {
  it("carries Leaflet inside it, the same version as the web's", () => {
    const html = page();
    expect(html).toContain(LEAFLET_JS);
    expect(html).toContain(LEAFLET_CSS);
    expect(LEAFLET_JS).toContain(`Leaflet ${LEAFLET_VERSION},`);
    expect(LEAFLET_VERSION).toBe(read("../../../web/package-lock.json").packages["node_modules/leaflet"].version);
  });

  it("keeps Leaflet from ending its own script or style block", () => {
    expect(LEAFLET_JS).not.toMatch(/<\/script/i);
    expect(LEAFLET_CSS).not.toMatch(/<\/style/i);
    expect(LEAFLET_JS).not.toContain("sourceMappingURL");
  });

  it("allows its own script and OSM's tiles, and nothing else", () => {
    expect(page()).toContain(
      `content="default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src https://tile.openstreetmap.org; base-uri 'none'; form-action 'none'"`,
    );
  });

  it("loads no script or style from anywhere", () => {
    expect(page().replace(LEAFLET_JS, "").replace(LEAFLET_CSS, "")).not.toMatch(/<script[^>]+src=|<link/i);
  });

  it("says its language", () => {
    expect(page([pin()], "ta")).toContain('<html lang="ta">');
  });

  it.each(["light", "dark"] as const)("draws the pins and the name in the %s theme's colours", (theme) => {
    const palette = colors[theme];
    const html = page([pin()], "en", palette).replace(LEAFLET_CSS, "");

    expect(html).toContain(`background: ${palette.primary}; color: ${palette.onPrimary}`);
    expect(html).toContain(`border: 2px solid ${palette.surface}`);
    expect(html).toContain(`.leaflet-popup-tip { background: ${palette.surface}; color: ${palette.fg}; }`);
    expect(html).toContain(`color: ${palette.brand}; font:`);
    expect(html).toContain(`html, body { height: 100%; margin: 0; background: ${palette.subtle}; }`);
    expect(pageData(html).data.ring).toBe(palette.primary);
  });

  it("gives the name and the zoom buttons a thumb's reach", () => {
    expect(page()).toContain(".open { display: block; min-height: 44px;");
    expect(page()).toContain(".leaflet-touch .leaflet-bar a { width: 44px; height: 44px; line-height: 44px; }");
  });

  it("cannot have its script closed by a place's name", () => {
    const name = '</script><img src=x onerror="window.pwned=1">';
    const { block, data } = pageData(page([pin({ name })]));

    expect(block).not.toContain("</script");
    expect(data.pins[0].name).toBe(name);
  });

  it("keeps line separators in a name from ending a line of its script", () => {
    const separators = String.fromCharCode(0x2028, 0x2029);
    const { block, data } = pageData(page([pin({ name: `a${separators}b` })]));

    expect(block).not.toContain(separators[0]);
    expect(block).not.toContain(separators[1]);
    expect(data.pins[0].name).toBe(`a${separators}b`);
  });
});

describe("the page's script", () => {
  it("labels the map, from data", () => {
    const { element } = runPage(page());
    expect(element.setAttribute).toHaveBeenCalledWith("aria-label", "Map of the places");
    expect(element.id).toBe("map");
  });

  it("draws the web's view of the searched circle, and fits it and every pin", () => {
    const { calls } = runPage(page([pin({ latitude: 8.1, longitude: 81.2 })]));

    expect(calls.map).toEqual({ zoomSnap: 0.5, attributionControl: false });
    expect(calls.circle).toEqual([
      { latitude: 6.927, longitude: 79.862 },
      { radius: 25_000, color: colors.light.primary, weight: 1.5, fillOpacity: 0.06, interactive: false },
    ]);
    expect(calls.fit.options).toEqual({ padding: [24, 24], maxZoom: 15, animate: false });
    expect(calls.fit.extended).toEqual([{ around: { latitude: 6.927, longitude: 79.862 }, metres: 50_000 }, [8.1, 81.2]]);
  });

  it("uses OSM's tile address and credit, with no prefix to link nowhere", () => {
    const { calls } = runPage(page());

    expect(calls.tiles.url).toBe("https://tile.openstreetmap.org/{z}/{x}/{y}.png");
    expect(calls.tiles.options).toEqual({ maxZoom: 19, attribution: pageData(page()).data.attribution });
    expect(calls.tiles.added).toBe(true);
    expect(calls.credit).toEqual({ options: { prefix: false }, added: true });
  });

  it("puts one numbered pin on each place, with its name for a screen reader", () => {
    const { calls } = runPage(page([pin({ name: "First" }), pin({ number: 2, slug: "b", name: "Second", latitude: 7, longitude: 80 })]));

    expect(calls.markers.map((marker: any) => marker.at)).toEqual([
      [6.9, 79.85],
      [7, 80],
    ]);
    expect(calls.markers.map((marker: any) => marker.options.title)).toEqual(["1. First", "2. Second"]);
    expect(calls.markers.map((marker: any) => marker.options.alt)).toEqual(["First", "Second"]);
    expect(calls.markers.every((marker: any) => marker.options.keyboard && marker.added)).toBe(true);
    expect(calls.markers.map((marker: any) => marker.options.icon)).toEqual([
      { className: "", html: '<span class="pin">1</span>', iconSize: [32, 32], iconAnchor: [16, 16] },
      { className: "", html: '<span class="pin">2</span>', iconSize: [32, 32], iconAnchor: [16, 16] },
    ]);
  });

  it("writes the name as text, and a tap on it asks for that place", () => {
    const name = "<b>Home</b>";
    const { calls, said } = runPage(page([pin({ name, slug: "home" })]));
    const { popup, popupOptions } = calls.markers[0];

    expect(popup.tag).toBe("button");
    expect(popup.type).toBe("button");
    expect(popup.className).toBe("open");
    expect(popup.textContent).toBe(name);
    expect(popup.innerHTML).toBeUndefined();
    expect(popupOptions).toEqual({ closeButton: false, maxWidth: 240 });

    said.length = 0;
    popup.click();
    expect(said).toEqual([{ type: "open", slug: "home" }]);
  });

  it("says it is ready only when everything is on the map", () => {
    const { said, order } = runPage(page());

    expect(said).toEqual([{ type: "ready" }]);
    expect(order).toEqual(["fit", "circle", "marker", "ready"]);
  });

  it("reports failing tiles once, however many fail", () => {
    const { calls, said } = runPage(page());
    said.length = 0;

    calls.tiles.error();
    calls.tiles.error();
    calls.tiles.error();

    expect(said).toEqual([{ type: "tiles" }]);
  });
});

describe("the words on the map", () => {
  it.each(["en", "si", "ta"])("are the web's in %s", (language) => {
    const app = read(`../i18n/messages/${language}.json`).App.near;
    const web = read(`../../../web/messages/${language}.json`).Find;

    expect(app.map).toBe(web.tabs.map);
    expect(app.mapTitle).toBe(web.map.label);
  });
});

/** The data block of a page, as the page's script block holds it. */
function pageData(html: string) {
  const opening = "<script>var MAP = ";
  const start = html.lastIndexOf(opening) + opening.length;
  const block = html.slice(start, html.indexOf(`;${MAP_SCRIPT}`));
  return { block, data: JSON.parse(block) };
}

/** Runs the page's own script text against a stand-in for Leaflet and the document. */
function runPage(html: string) {
  const said: Record<string, string>[] = [];
  const order: string[] = [];
  const element = { id: "", setAttribute: jest.fn() };
  const calls: any = { markers: [] };

  const layer = (record: any) => ({
    addTo() {
      record.added = true;
      return this;
    },
    on(event: string, handler: () => void) {
      if (event === "tileerror") record.error = handler;
      return this;
    },
    bindPopup(popup: any, options: any) {
      record.popup = popup;
      record.popupOptions = options;
      return this;
    },
  });

  const L = {
    map: (id: string, options: any) => {
      element.id = id;
      calls.map = options;
      return {
        fitBounds(bounds: any, fit: any) {
          order.push("fit");
          calls.fit = { extended: bounds.extended, options: fit };
        },
      };
    },
    control: { attribution: (options: any) => layer((calls.credit = { options })) },
    tileLayer: (url: string, options: any) => layer((calls.tiles = { url, options })),
    latLng: (latitude: number, longitude: number) => ({
      latitude,
      longitude,
      toBounds: (metres: number) => ({ around: { latitude, longitude }, metres }),
    }),
    latLngBounds: () => {
      const extended: unknown[] = [];
      return { extended, extend: (value: unknown) => extended.push(value) };
    },
    circle: (centre: any, options: any) => {
      order.push("circle");
      calls.circle = [{ latitude: centre.latitude, longitude: centre.longitude }, options];
      return layer({});
    },
    divIcon: (options: unknown) => options,
    marker: (at: number[], options: any) => {
      if (!order.includes("marker")) order.push("marker");
      const record = { at, options };
      calls.markers.push(record);
      return layer(record);
    },
  };

  const document = {
    getElementById: () => element,
    createElement: (tag: string) => {
      let onClick = () => {};
      return {
        tag,
        addEventListener: (event: string, handler: () => void) => {
          if (event === "click") onClick = handler;
        },
        click: () => onClick(),
      };
    },
  };

  const window = {
    ReactNativeWebView: {
      postMessage(text: string) {
        const message = JSON.parse(text);
        if (message.type === "ready") order.push("ready");
        said.push(message);
      },
    },
  };

  const { block } = pageData(html);
  new Function("L", "window", "document", `var MAP = ${block};${MAP_SCRIPT}`)(L, window, document);
  return { calls, said, order, element };
}
