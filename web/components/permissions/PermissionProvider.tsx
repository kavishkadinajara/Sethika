"use client";

import { createContext, useMemo, type ReactNode } from "react";
import type { EffectivePermissions, PermissionCode } from "@/lib/permissions/types";

export interface PermissionContextValue {
  can: (code: PermissionCode) => boolean;
  canAll: (codes: PermissionCode[]) => boolean;
  canAny: (codes: PermissionCode[]) => boolean;
  isPlatform: boolean;
  institutionId: number | null;
  isAuthenticated: boolean;
}

export const PermissionContext = createContext<PermissionContextValue | null>(null);

export function PermissionProvider({
  effective,
  children,
}: {
  effective: EffectivePermissions | null;
  children: ReactNode;
}) {
  const value = useMemo<PermissionContextValue>(() => {
    const held = new Set(
      (effective?.permissions ?? []).map((code) => code.toUpperCase()),
    );

    const can = (code: PermissionCode) =>
      typeof code === "string" && code.length > 0 && held.has(code.toUpperCase());

    return {
      can,
      canAll: (codes) => codes.every(can),
      canAny: (codes) => codes.some(can),
      isPlatform: effective?.isPlatform ?? false,
      institutionId: effective?.institutionId ?? null,
      isAuthenticated: effective !== null,
    };
  }, [effective]);

  return (
    <PermissionContext.Provider value={value}>{children}</PermissionContext.Provider>
  );
}
