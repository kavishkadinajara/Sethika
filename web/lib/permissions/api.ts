import { apiFetch } from "@/lib/auth";
import type { EffectivePermissions } from "./types";

/**
 * GET /api/me/permissions through the session's fetch wrapper (Bearer token, refresh on 401).
 * Null when signed out. The answer only decides what the UI shows; every endpoint still
 * checks its own [HasPermission].
 */
export async function fetchEffectivePermissions(): Promise<EffectivePermissions | null> {
  const response = await apiFetch("/api/me/permissions", { cache: "no-store" });

  if (response.status === 401) {
    return null;
  }

  if (!response.ok) {
    throw new Error(`Failed to load permissions: ${response.status} ${response.statusText}`);
  }

  return (await response.json()) as EffectivePermissions;
}
