"use client";

import type { ReactNode } from "react";
import { usePermissions } from "@/lib/permissions/usePermissions";

type CanProps = {
  children: ReactNode;
  fallback?: ReactNode;
} & (
  | { permission: string; anyOf?: never; allOf?: never }
  | { permission?: never; anyOf: string[]; allOf?: never }
  | { permission?: never; anyOf?: never; allOf: string[] }
);

export function Can({ children, fallback = null, ...rule }: CanProps) {
  const { can, canAny, canAll } = usePermissions();

  const allowed =
    rule.permission !== undefined
      ? can(rule.permission)
      : rule.anyOf !== undefined
        ? canAny(rule.anyOf)
        : canAll(rule.allOf);

  return <>{allowed ? children : fallback}</>;
}
