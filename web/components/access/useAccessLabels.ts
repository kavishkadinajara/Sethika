"use client";

import { useTranslations } from "next-intl";
import { useCallback, useMemo } from "react";
import type { InstitutionRole } from "@/lib/access/types";

/**
 * The catalogue's names in the reader's language. auth.Modules, Screens, PermissionActions and
 * RoleLevels hold English names only, so each is looked up by its code in the Access messages
 * and the database's name is the fallback for a code added after these translations. A custom
 * role's name is what the institution typed; only the built-in roles are translated.
 */
export function useAccessLabels() {
  const t = useTranslations("Access");

  const lookup = useCallback(
    (key: string, fallback: string) => (t.has(key as never) ? t(key as never) : fallback),
    [t],
  );

  return useMemo(
    () => ({
      module: (code: string, fallback: string) => lookup(`modules.${code}`, fallback),
      /** Screen codes are "MODULE.SCREEN", which the messages nest the same way. */
      screen: (code: string, fallback: string) => lookup(`screens.${code}`, fallback),
      action: (code: string) => lookup(`actions.${code}`, code),
      level: (code: string, fallback: string) => lookup(`levels.${code}`, fallback),
      role: (role: Pick<InstitutionRole, "code" | "name" | "isSystem">) =>
        role.isSystem ? lookup(`systemRoles.${role.code}`, role.name) : role.name,
    }),
    [lookup],
  );
}
