"use client";

import { useEffect, useRef } from "react";
import { watchAvailability, type AvailabilityChanged } from "@/lib/bookings/live";

/**
 * Calls `onChange` for each change to `slug`'s days while the component is mounted (plan 07, H11).
 * `null` watches nothing. The latest `onChange` is called, so it may read the current month.
 */
export function useLiveAvailability(slug: string | null, onChange: (change: AvailabilityChanged) => void) {
  const latest = useRef(onChange);
  useEffect(() => {
    latest.current = onChange;
  });

  useEffect(() => {
    if (!slug) return;
    return watchAvailability(slug, (change) => latest.current(change));
  }, [slug]);
}
