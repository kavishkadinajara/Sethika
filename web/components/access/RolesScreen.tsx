"use client";

import { useTranslations } from "next-intl";
import { useCallback, useEffect, useState } from "react";
import { FormError } from "@/components/auth/FormError";
import { Button } from "@/components/ui/button";
import { KeyIcon, LockIcon, PlusIcon } from "@/components/ui/icons";
import { Spinner } from "@/components/ui/spinner";
import { useToast } from "@/components/ui/toast";
import { getCatalogue, getRoleLevels, listRoles } from "@/lib/access/api";
import type { CatalogueModule, GrantableLevel, InstitutionRole } from "@/lib/access/types";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { cn, focusRing } from "@/lib/utils";
import { CreateRoleSheet } from "./CreateRoleSheet";
import { RoleEditor } from "./RoleEditor";
import { useAccessError } from "./useAccessError";
import { useAccessLabels } from "./useAccessLabels";

type Loaded = { roles: InstitutionRole[]; catalogue: CatalogueModule[]; levels: GrantableLevel[] };

/**
 * The institution's roles (plan 04, U3): the list beside the chosen role's permission matrix.
 * Roles at the caller's level or above are shown but locked, so a manager sees what an owner may
 * do without being able to change it.
 */
export function RolesScreen() {
  const t = useTranslations("Access.roles");
  const tCreate = useTranslations("Access.create");
  const labels = useAccessLabels();
  const errorMessage = useAccessError();
  const toast = useToast();
  const { can } = usePermissions();

  const [loaded, setLoaded] = useState<Loaded | null>(null);
  const [failure, setFailure] = useState<string | null>(null);
  const [chosen, setChosen] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);

  const load = useCallback(async () => {
    const [roles, catalogue, levels] = await Promise.all([listRoles(), getCatalogue(), getRoleLevels()]);
    if (!roles.ok) return setFailure(errorMessage(roles.failure));
    if (!catalogue.ok) return setFailure(errorMessage(catalogue.failure));
    // A caller who may view roles but holds no rank here gets no levels; they can still look.
    setLoaded({ roles: roles.value, catalogue: catalogue.value, levels: levels.ok ? levels.value : [] });
  }, [errorMessage]);

  useEffect(() => {
    // Loading on mount; the state is set when the answers arrive, never synchronously here.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load]);

  if (failure) return <FormError>{failure}</FormError>;
  if (!loaded) {
    return (
      <div className="flex justify-center py-16 text-brand">
        <Spinner />
      </div>
    );
  }

  const { roles, catalogue, levels } = loaded;
  const role = roles.find((r) => r.code === chosen) ?? roles.find((r) => r.canManage) ?? roles[0] ?? null;
  const grantable = new Set(levels.find((l) => l.level === role?.level)?.permissions ?? []);

  return (
    <div className="grid gap-8">
      {can("ACCESS.ROLES.CREATE") && levels.length > 0 && (
        <div>
          <Button onClick={() => setCreating(true)}>
            <PlusIcon className="size-5" />
            {t("newRole")}
          </Button>
        </div>
      )}

      <div className="grid items-start gap-8 lg:grid-cols-[minmax(16rem,22rem)_minmax(0,1fr)]">
        <nav aria-label={t("list")} className="lg:sticky lg:top-24">
          <ul className="grid gap-2">
            {roles.map((r) => {
              const here = r.code === role?.code;
              return (
                <li key={r.code}>
                  <button
                    type="button"
                    aria-current={here ? "true" : undefined}
                    onClick={() => setChosen(r.code)}
                    className={cn(
                      "relative grid w-full gap-1 rounded-bowl border px-4 py-3 text-left transition-colors",
                      focusRing,
                      here ? "border-primary bg-primary/8 shadow-lift" : "border-hairline bg-surface hover:border-line",
                      here && "before:absolute before:inset-y-3 before:left-0 before:w-[3px] before:rounded-r-full before:bg-accent",
                    )}
                  >
                    <span className="flex items-center justify-between gap-2">
                      <span className="inline-flex min-w-0 items-center gap-2 font-semibold text-fg">
                        <KeyIcon className="size-4.5 shrink-0 text-brand" />
                        <span className="truncate">{labels.role(r)}</span>
                      </span>
                      {!r.canManage && <LockIcon className="size-4 shrink-0 text-muted" aria-label={t("locked")} />}
                    </span>
                    <span className="flex flex-wrap gap-x-3 text-sm text-muted">
                      <span>{labels.level(r.level, r.levelName)}</span>
                      <span>{t("members", { count: r.members })}</span>
                      <span>{t("permissionCount", { count: r.permissions.length })}</span>
                      {r.isSystem && <span>{t("system")}</span>}
                    </span>
                  </button>
                </li>
              );
            })}
          </ul>
        </nav>

        <div className="min-w-0">
          {role ? (
            <RoleEditor
              // A new key after each save: the editor starts again from what was saved.
              key={`${role.code}:${role.permissions.join(",")}`}
              role={role}
              catalogue={catalogue}
              grantable={grantable}
              editable={role.canManage && can("ACCESS.ROLES.EDIT")}
              onSaved={(permissions, version) =>
                setLoaded((current) =>
                  current && {
                    ...current,
                    roles: current.roles.map((r) => (r.code === role.code ? { ...r, permissions, version } : r)),
                  },
                )
              }
            />
          ) : (
            <p className="text-muted">{t("empty")}</p>
          )}
        </div>
      </div>

      {creating && (
        <CreateRoleSheet
          levels={levels}
          roles={roles}
          onClose={() => setCreating(false)}
          onCreated={(code, name) => {
            setCreating(false);
            setChosen(code);
            toast({ variant: "success", title: tCreate("created", { role: name }) });
            void load();
          }}
        />
      )}
    </div>
  );
}
