"use client";

import { useEffect, useState } from "react";
import { getToday } from "@/lib/bookings/api";
import type { TodaysTable } from "@/lib/bookings/types";
import type { ShellSummary } from "@/lib/shell/nav";
import { getSummary, onShellStale } from "@/lib/shell/summary";

/** How often the sidebar asks again while the tab is visible (plan 05, R3; SignalR replaces it in Phase C). */
const POLL_MS = 60_000;

export type ShellData = { summary: ShellSummary | null; today: TodaysTable | null };

type Loaded = ShellData & { key: string };

/**
 * The sidebar's live parts: the badge counts and, for those who may read the calendar, today's
 * table. Read when the context starts, every minute while the tab is visible, when the window
 * regains focus, and after a screen's own decision (markShellStale). Each answer belongs to the
 * context it was asked in (`contextKey`): after a switch, nothing of the old institution shows.
 * A failed read keeps what was there; the next one tries again.
 */
export function useShellData({
  enabled,
  withTable,
  contextKey,
}: {
  enabled: boolean;
  withTable: boolean;
  contextKey: string;
}): ShellData {
  const [loaded, setLoaded] = useState<Loaded | null>(null);

  useEffect(() => {
    if (!enabled) return;

    // An answer that arrives after the context changed (or the sidebar left) is dropped here, so
    // a slow read for the old institution never overwrites the new one's.
    let cancelled = false;
    const load = async () => {
      const [summary, today] = await Promise.all([getSummary(), withTable ? getToday() : Promise.resolve(null)]);
      if (cancelled) return;
      setLoaded((previous) => {
        const kept = previous?.key === contextKey ? previous : null;
        return {
          key: contextKey,
          summary: summary.ok ? summary.value : (kept?.summary ?? null),
          today: today === null ? null : today.ok ? today.value : (kept?.today ?? null),
        };
      });
    };
    const refresh = () => {
      if (document.visibilityState === "visible") void load();
    };

    refresh();
    const timer = window.setInterval(refresh, POLL_MS);
    window.addEventListener("focus", refresh);
    document.addEventListener("visibilitychange", refresh);
    const unsubscribe = onShellStale(refresh);

    return () => {
      cancelled = true;
      window.clearInterval(timer);
      window.removeEventListener("focus", refresh);
      document.removeEventListener("visibilitychange", refresh);
      unsubscribe();
    };
  }, [enabled, withTable, contextKey]);

  return enabled && loaded?.key === contextKey
    ? { summary: loaded.summary, today: loaded.today }
    : { summary: null, today: null };
}
