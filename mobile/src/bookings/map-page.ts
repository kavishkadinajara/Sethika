import type { Language } from "@/i18n/translate";
import type { Palette } from "@/theme/colors";

import { LEAFLET_CSS, LEAFLET_JS } from "./leaflet-bundle";
import { localName } from "./names";
import type { NearPoint } from "./near-me";
import type { SearchPlace } from "./types";

/*  The near-me map as a page for a WebView (F34), without the WebView: the web's Leaflet map over
    OpenStreetMap, Leaflet inside the page. OSM's tile policy shaped three things here (F69): the
    page's address gives its tile requests a Referer, the app names itself in a User-Agent of its
    own, and the credit stays on the map. The tiles keep their colours in dark, as on the web.   */

/** The page's address. Nothing is fetched from it: it is what OSM reads as the Referer. */
export const MAP_BASE_URL = "https://sethika.com/";

const TILES = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";

/** OSM's licence page, the one link the page may send out of the app. */
export const OSM_COPYRIGHT = "https://www.openstreetmap.org/copyright";

/** The policy blocks a library's default and forbids posing as a browser, which a WebView's own does. */
export function mapUserAgent(version: string | null | undefined): string {
  return `Sethika/${version || "0"} (+https://sethika.com)`;
}

/** What happens to an address the page tries to open: only the page itself loads in the WebView. */
export function mapNavigation(url: string): "load" | "browser" | "block" {
  if (url === MAP_BASE_URL || url === "about:blank") return "load";
  return url === OSM_COPYRIGHT ? "browser" : "block";
}

export type MapPin = { number: number; slug: string; name: string; latitude: number; longitude: number };

/** One pin per place, in the list's order, so a pin and its card share a number. */
export function mapPins(places: SearchPlace[], language: Language): MapPin[] {
  return places.map((place, index) => ({
    number: index + 1,
    slug: place.slug,
    name: localName(language, place),
    latitude: place.latitude,
    longitude: place.longitude,
  }));
}

export type MapMessage = { type: "ready" } | { type: "tiles" } | { type: "open"; slug: string };

/** What the page said, or null. A slug only counts if it is one of the pins the app gave it. */
export function readMapMessage(data: string, pins: MapPin[]): MapMessage | null {
  let message: { type?: unknown; slug?: unknown } | null;
  try {
    message = JSON.parse(data);
  } catch {
    return null;
  }
  if (message?.type === "ready" || message?.type === "tiles") return { type: message.type };
  if (message?.type !== "open") return null;
  const pin = pins.find((candidate) => candidate.slug === message.slug);
  return pin ? { type: "open", slug: pin.slug } : null;
}

/** Runs in the page, as text: Hermes keeps no function source to send. Reads `L` and `MAP`. */
export const MAP_SCRIPT = `
(function () {
  function say(message) {
    window.ReactNativeWebView.postMessage(JSON.stringify(message));
  }

  document.getElementById("map").setAttribute("aria-label", MAP.label);
  var map = L.map("map", { zoomSnap: 0.5, attributionControl: false });
  // No Leaflet prefix: its link would go nowhere, and OSM's credit is the one the licence asks for.
  L.control.attribution({ prefix: false }).addTo(map);

  var told = false;
  L.tileLayer(MAP.tiles, { maxZoom: 19, attribution: MAP.attribution })
    .on("tileerror", function () {
      if (told) return;
      told = true;
      say({ type: "tiles" });
    })
    .addTo(map);

  var centre = L.latLng(MAP.latitude, MAP.longitude);
  var bounds = L.latLngBounds([]);
  bounds.extend(centre.toBounds(MAP.radiusKm * 2000));
  MAP.pins.forEach(function (pin) {
    bounds.extend([pin.latitude, pin.longitude]);
  });
  map.fitBounds(bounds, { padding: [24, 24], maxZoom: 15, animate: false });

  L.circle(centre, { radius: MAP.radiusKm * 1000, color: MAP.ring, weight: 1.5, fillOpacity: 0.06, interactive: false }).addTo(map);

  MAP.pins.forEach(function (pin) {
    var icon = L.divIcon({ className: "", html: '<span class="pin">' + pin.number + "</span>", iconSize: [32, 32], iconAnchor: [16, 16] });
    // The name goes in as text, never as markup.
    var open = document.createElement("button");
    open.type = "button";
    open.className = "open";
    open.textContent = pin.name;
    open.addEventListener("click", function () {
      say({ type: "open", slug: pin.slug });
    });
    L.marker([pin.latitude, pin.longitude], { icon: icon, title: pin.number + ". " + pin.name, alt: pin.name, keyboard: true })
      .bindPopup(open, { closeButton: false, maxWidth: 240 })
      .addTo(map);
  });

  say({ type: "ready" });
})();
`;

/** A value for a script block: a place's name must not be able to close the block. */
function asScript(value: unknown): string {
  return JSON.stringify(value).replace(/[<\u2028\u2029]/g, (c) => `\\u${c.charCodeAt(0).toString(16).padStart(4, "0")}`);
}

type MapPageInput = { language: Language; palette: Palette; label: string; point: NearPoint; radiusKm: number; pins: MapPin[] };

/** The whole page. Its policy lets it run its own script and load OSM's tiles, and nothing else. */
export function mapPage({ language, palette, label, point, radiusKm, pins }: MapPageInput): string {
  const data = {
    label,
    tiles: TILES,
    attribution: `&copy; <a href="${OSM_COPYRIGHT}">OpenStreetMap</a> contributors`,
    latitude: point.latitude,
    longitude: point.longitude,
    radiusKm,
    ring: palette.primary,
    pins,
  };

  return `<!doctype html>
<html lang="${language}">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src https://tile.openstreetmap.org; base-uri 'none'; form-action 'none'">
<style>${LEAFLET_CSS}</style>
<style>
html, body { height: 100%; margin: 0; background: ${palette.subtle}; }
#map { position: absolute; inset: 0; background: ${palette.subtle}; font-family: system-ui, sans-serif; }
.pin { display: grid; place-items: center; box-sizing: border-box; width: 32px; height: 32px; border: 2px solid ${palette.surface}; border-radius: 50%; background: ${palette.primary}; color: ${palette.onPrimary}; font-size: 14px; font-weight: 600; box-shadow: 0 1px 4px rgba(0, 0, 0, 0.4); }
.leaflet-popup-content-wrapper, .leaflet-popup-tip { background: ${palette.surface}; color: ${palette.fg}; }
.leaflet-popup-content { margin: 4px 8px; }
.open { display: block; min-height: 44px; padding: 8px; border: 0; background: none; color: ${palette.brand}; font: 600 15px/1.65 system-ui, sans-serif; text-align: left; text-decoration: underline; overflow-wrap: break-word; }
.leaflet-touch .leaflet-bar a { width: 44px; height: 44px; line-height: 44px; }
</style>
</head>
<body>
<div id="map" role="region"></div>
<script>${LEAFLET_JS}</script>
<script>var MAP = ${asScript(data)};${MAP_SCRIPT}</script>
</body>
</html>`;
}
