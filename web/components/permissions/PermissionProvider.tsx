"use client";

import { createContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { useSession } from "@/lib/auth/useSession";
import { fetchEffectivePermissions } from "@/lib/permissions/api";
import type { EffectivePermissions, PermissionCode } from "@/lib/permissions/types";

export interface PermissionContextValue {
  can: (code: PermissionCode) => boolean;
  canAll: (codes: PermissionCode[]) => boolean;
  canAny: (codes: PermissionCode[]) => boolean;
  isPlatform: boolean;
  institutionId: number | null;
  /** The signed-in user's id (the token's sub), or null; screens use it to recognise "you". */
  userId: string | null;
  isAuthenticated: boolean;
  /** True until the session and its permissions are known. Every gate is closed meanwhile. */
  isLoading: boolean;
}

export const PermissionContext = createContext<PermissionContextValue | null>(null);

type Loaded = { key: string; effective: EffectivePermissions | null };

/**
 * Loads the signed-in user's effective permissions in the browser, after the silent refresh,
 * through the session's fetch wrapper. They are loaded again when the session starts, ends or
 * moves to another institution.
 */
export function PermissionProvider({ children }: { children: ReactNode }) {
  const session = useSession();
  const key =
    session.status === "authenticated" ? `in:${session.activeInstitution ?? ""}` : session.status;
  const [loaded, setLoaded] = useState<Loaded | null>(null);

  useEffect(() => {
    if (session.status !== "authenticated") return;

    let cancelled = false;
    fetchEffectivePermissions()
      .catch((error: unknown) => {
        console.error("[permissions] could not load effective permissions", error);
        return null;
      })
      .then((effective) => {
        if (!cancelled) setLoaded({ key, effective });
      });

    return () => {
      cancelled = true;
    };
  }, [key, session.status]);

  // Only an answer for the current session counts; a stale one would open the wrong gates.
  const effective = session.status === "authenticated" && loaded?.key === key ? loaded.effective : null;
  const isLoading =
    session.status === "loading" || (session.status === "authenticated" && loaded?.key !== key);

  const value = useMemo<PermissionContextValue>(() => {
    const held = new Set((effective?.permissions ?? []).map((code) => code.toUpperCase()));

    const can = (code: PermissionCode) =>
      typeof code === "string" && code.length > 0 && held.has(code.toUpperCase());

    return {
      can,
      canAll: (codes) => codes.every(can),
      canAny: (codes) => codes.some(can),
      isPlatform: effective?.isPlatform ?? false,
      institutionId: effective?.institutionId ?? null,
      userId: effective?.userId ?? null,
      isAuthenticated: effective !== null,
      isLoading,
    };
  }, [effective, isLoading]);

  return <PermissionContext.Provider value={value}>{children}</PermissionContext.Provider>;
}
