"use client";

import { useSyncExternalStore } from "react";

const query = "(min-width: 40rem)";

function subscribe(onChange: () => void) {
  const media = window.matchMedia(query);
  media.addEventListener("change", onChange);
  return () => media.removeEventListener("change", onChange);
}

/**
 * Whether the screen is wider than a phone (Tailwind's `sm`): a sheet then comes from the side,
 * not from the bottom where a thumb reaches it. The server renders the phone's.
 */
export function useWide(): boolean {
  return useSyncExternalStore(
    subscribe,
    () => window.matchMedia(query).matches,
    () => false,
  );
}
