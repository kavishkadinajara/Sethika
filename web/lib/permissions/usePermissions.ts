"use client";

import { useContext } from "react";
import {
  PermissionContext,
  type PermissionContextValue,
} from "@/components/permissions/PermissionProvider";

export function usePermissions(): PermissionContextValue {
  const context = useContext(PermissionContext);

  if (context === null) {
    throw new Error("usePermissions must be used inside a <PermissionProvider>.");
  }

  return context;
}

/** Convenience for the common single-code case. */
export function usePermission(code: string): boolean {
  return usePermissions().can(code);
}
