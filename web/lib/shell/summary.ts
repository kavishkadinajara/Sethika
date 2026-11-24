import { json, send, type OnboardingResult } from "@/lib/onboarding/api";
import type { ShellSummary } from "./nav";

/** GET /api/me/summary: the sidebar's counts, each null where the caller may not see its screen. */
export async function getSummary(): Promise<OnboardingResult<ShellSummary>> {
  return json(await send("/api/me/summary", { method: "GET" }));
}

const STALE_EVENT = "sethika:shell-stale";

/**
 * Tells the sidebar its counts and today's table may have changed: a screen calls it after a
 * decision it made itself, so the badge does not wait for the next minute's poll.
 */
export function markShellStale(): void {
  if (typeof window !== "undefined") window.dispatchEvent(new Event(STALE_EVENT));
}

/** Calls `listener` whenever markShellStale() is called; returns the unsubscribe. */
export function onShellStale(listener: () => void): () => void {
  window.addEventListener(STALE_EVENT, listener);
  return () => window.removeEventListener(STALE_EVENT, listener);
}
