"use client";

import { useCallback, useSyncExternalStore } from "react";

/** Whether the window matches the media `query`. The server renders as if it did not. */
export function useMediaQuery(query: string): boolean {
  const subscribe = useCallback(
    (onChange: () => void) => {
      const media = window.matchMedia(query);
      media.addEventListener("change", onChange);
      return () => media.removeEventListener("change", onChange);
    },
    [query],
  );

  return useSyncExternalStore(
    subscribe,
    () => window.matchMedia(query).matches,
    () => false,
  );
}

/**
 * Whether the screen is wider than a phone (Tailwind's `sm`): a sheet then comes from the side,
 * not from the bottom where a thumb reaches it. The server renders the phone's.
 */
export function useWide(): boolean {
  return useMediaQuery("(min-width: 40rem)");
}
