import * as Location from "expo-location";

import type { LocationDevice, NearPoint } from "./near-me";

/*  The real phone behind near-me.ts. Foreground only (F33): nothing here asks for the background
    permission or watches the position, and app.json keeps the background switches off.          */

/** As old as a position may be, as on the web: five minutes. */
const LAST_KNOWN_MAX_AGE_MS = 300_000;

const pointOf = ({ coords }: Location.LocationObject): NearPoint => ({ latitude: coords.latitude, longitude: coords.longitude });

export const nativeLocation: LocationDevice = {
  // Shows the phone's question only if it has not been answered, so a tap is all that ever asks.
  requestPermission: () => Location.requestForegroundPermissionsAsync().then((answer) => answer.granted),
  lastKnown: () => Location.getLastKnownPositionAsync({ maxAge: LAST_KNOWN_MAX_AGE_MS }).then((known) => known && pointOf(known)),
  // Balanced is about 100 m, what the rounding keeps anyway, and comes sooner than GPS indoors.
  current: () => Location.getCurrentPositionAsync({ accuracy: Location.Accuracy.Balanced }).then(pointOf),
};
