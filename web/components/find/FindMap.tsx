"use client";

import L from "leaflet";
import "leaflet/dist/leaflet.css";
import { useTranslations } from "next-intl";
import { useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { inSriLanka, roundCoordinate, type Where } from "@/lib/bookings/find-query";
import type { SearchPlace } from "@/lib/bookings/types";
import { cn } from "@/lib/utils";

/** Sri Lanka, for a map with nothing on it yet. */
const island = L.latLngBounds([5.9, 79.5], [9.9, 81.9]);

type FindMapProps = {
  places: SearchPlace[];
  where: Where;
  /** The place's name in the page's language. */
  nameOf: (place: SearchPlace) => string;
  /** Its page, in the page's language. */
  hrefOf: (place: SearchPlace) => string;
  /** The donor moved the map and asked to search there: its centre, rounded. */
  onSearchHere: (latitude: number, longitude: number) => void;
};

/**
 * The results on OpenStreetMap (plan 06, G10): one numbered marker per place, the same number as
 * its card in the list, and the searched circle. Leaflet alone, loaded only in the browser (the
 * screen imports this with `ssr: false`). Our own markers, not Leaflet's images: a numbered disc
 * in the primary colour, faith-neutral, readable in both themes.
 *
 * Moving the map offers "Search this area", which searches around its centre (G3). The list is
 * the page's real content; the map is a second view of it, so a marker's popup links to the same
 * page its card does, and its name goes in as text, never as markup.
 */
export default function FindMap({ places, where, nameOf, hrefOf, onSearchHere }: FindMapProps) {
  const t = useTranslations("Find.map");
  const container = useRef<HTMLDivElement>(null);
  const map = useRef<L.Map | null>(null);
  const layer = useRef<L.LayerGroup | null>(null);
  const [moved, setMoved] = useState(false);
  const [outside, setOutside] = useState(false);

  useEffect(() => {
    if (!container.current) return;
    const created = L.map(container.current, { scrollWheelZoom: false, zoomSnap: 0.5 });
    L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
      maxZoom: 19,
      attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
    }).addTo(created);
    created.on("moveend", () => {
      setMoved(true);
      setOutside(false);
    });
    layer.current = L.layerGroup().addTo(created);
    map.current = created;
    return () => {
      created.remove();
      map.current = null;
    };
  }, []);

  useEffect(() => {
    const current = map.current;
    const group = layer.current;
    if (!current || !group) return;
    group.clearLayers();

    const bounds = L.latLngBounds([]);
    if (where.kind === "near") {
      const centre = L.latLng(where.latitude, where.longitude);
      L.circle(centre, {
        radius: where.radiusKm * 1000,
        className: "stroke-primary fill-primary",
        weight: 1.5,
        fillOpacity: 0.06,
        interactive: false,
      }).addTo(group);
      // Measured from its centre, not asked of the circle: Leaflet adds a layer only once the map
      // has a view, and before that the circle has no map to measure itself on.
      bounds.extend(centre.toBounds(where.radiusKm * 2000));
    }

    places.forEach((place, index) => {
      const name = nameOf(place);
      const icon = L.divIcon({
        className: "",
        html: `<span class="grid size-8 place-items-center rounded-full border-2 border-surface bg-primary text-sm font-semibold text-on-primary shadow-lift">${index + 1}</span>`,
        iconSize: [32, 32],
        iconAnchor: [16, 16],
      });

      const popup = document.createElement("a");
      popup.href = hrefOf(place);
      popup.textContent = name;
      popup.className = "font-semibold text-brand underline";

      L.marker([place.latitude, place.longitude], { icon, title: `${index + 1}. ${name}`, alt: name, keyboard: true })
        .bindPopup(popup)
        .addTo(group);
      bounds.extend([place.latitude, place.longitude]);
    });

    // Unanimated, the fit's own "moveend" fires inside this call; clearing `moved` after it leaves
    // only the donor's own moves offering a search.
    current.fitBounds(bounds.isValid() ? bounds : island, { padding: [24, 24], maxZoom: 15, animate: false });
    setMoved(false);
  }, [places, where, nameOf, hrefOf]);

  function searchHere() {
    const centre = map.current?.getCenter();
    if (!centre) return;
    const latitude = roundCoordinate(centre.lat);
    const longitude = roundCoordinate(centre.lng);
    if (!inSriLanka(latitude, longitude)) {
      setOutside(true);
      return;
    }
    onSearchHere(latitude, longitude);
  }

  // isolate: Leaflet's panes carry z-indexes up to 1000; inside their own stacking context they
  // cannot rise over the app's sheets and menus.
  return (
    <div className="relative isolate">
      <div
        ref={container}
        role="region"
        aria-label={t("label")}
        className="z-0 h-104 w-full overflow-hidden rounded-bowl border border-hairline bg-subtle lg:h-144"
      />
      <div className={cn("pointer-events-none absolute inset-x-0 top-3 z-1000 flex justify-center px-3", !moved && "hidden")}>
        <div className="pointer-events-auto grid justify-items-center gap-2">
          <Button type="button" variant="primary" onClick={searchHere}>
            {t("searchHere")}
          </Button>
          {outside && (
            <p role="status" className="rounded-cup bg-surface px-3 py-2 text-sm text-fg shadow-lift">
              {t("outside")}
            </p>
          )}
        </div>
      </div>
    </div>
  );
}
