"use client";

import { useTranslations } from "next-intl";
import { useMemo, useState } from "react";
import { FormError } from "@/components/auth/FormError";
import { Button } from "@/components/ui/button";
import { CheckIcon, LockIcon, ShieldIcon } from "@/components/ui/icons";
import { useToast } from "@/components/ui/toast";
import { setRolePermissions } from "@/lib/access/api";
import { canTurnOn, changes, indexCatalogue, toggle } from "@/lib/access/matrix";
import type { CatalogueModule, InstitutionRole } from "@/lib/access/types";
import { cn } from "@/lib/utils";
import { useAccessError } from "./useAccessError";
import { useAccessLabels } from "./useAccessLabels";

type Props = {
  role: InstitutionRole;
  catalogue: CatalogueModule[];
  /** What the caller may give at this role's level: its ceiling ∩ what they hold (/role-levels). */
  grantable: ReadonlySet<string>;
  /** The caller may change this role: more senior, and holds ACCESS.ROLES.EDIT. */
  editable: boolean;
  onSaved: (permissions: string[]) => void;
};

/**
 * What a role may do, as the institution's modules → screens → actions (plan 04, X7). Each
 * action is a pill: ticking one ticks what it needs, unticking a VIEW unticks what needs it, and a
 * pill the caller may not turn on says why. The API checks everything again on save; the step-up
 * that ACCESS.ROLES.EDIT needs is asked for by apiFetch when the save meets it.
 */
export function RoleEditor({ role, catalogue, grantable, editable, onSaved }: Props) {
  const t = useTranslations("Access.roles");
  const labels = useAccessLabels();
  const errorMessage = useAccessError();
  const toast = useToast();
  const index = useMemo(() => indexCatalogue(catalogue), [catalogue]);

  const [selected, setSelected] = useState<Set<string>>(() => new Set(role.permissions));
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<{ message: string; codes: string[] } | null>(null);
  const pending = changes(role.permissions, selected);
  const dirty = pending.added.length + pending.removed.length;
  const name = labels.role(role);

  async function save() {
    setSaving(true);
    setError(null);
    const permissions = [...selected].sort();
    const result = await setRolePermissions(role.code, permissions);
    setSaving(false);
    if (!result.ok) {
      setError({ message: errorMessage(result.failure), codes: result.failure.fields });
      return;
    }
    toast({ variant: "success", title: t("saved", { role: name }) });
    onSaved(permissions);
  }

  return (
    <section aria-labelledby="role-editor-title" className="grid gap-6">
      <header className="grid gap-2">
        <h2 id="role-editor-title" className="text-2xl font-semibold text-brand">
          {editable ? t("editing", { role: name }) : t("readOnly", { role: name })}
        </h2>
        <p className="flex flex-wrap items-center gap-2 text-sm text-muted">
          <span className="rounded-full bg-subtle px-3 py-1 font-medium text-fg">
            {t("level")}: {labels.level(role.level, role.levelName)}
          </span>
          <span>{t("members", { count: role.members })}</span>
          {!role.canManage && (
            <span className="inline-flex items-center gap-1.5">
              <LockIcon className="size-4" />
              {t("locked")}
            </span>
          )}
        </p>
      </header>

      <FormError>{error?.message}</FormError>

      <div className="grid gap-6">
        {catalogue.map((module) => (
          <fieldset key={module.code} className="rounded-bowl border border-hairline bg-surface p-4 shadow-lift sm:p-5">
            <legend className="px-2 text-sm font-semibold tracking-wide text-muted uppercase">{labels.module(module.code, module.name)}</legend>
            <div className="divide-y divide-hairline">
              {module.screens.map((screen) => {
                const screenName = labels.screen(screen.code, screen.name);
                return (
                  <div key={screen.code} className="grid items-start gap-3 py-3 first:pt-1 last:pb-1 md:grid-cols-[minmax(12rem,1fr)_2fr]">
                    <p className="pt-2.5 font-medium text-fg">{screenName}</p>
                    <div role="group" aria-label={screenName} className="flex flex-wrap gap-2">
                      {screen.permissions.map((permission) => {
                        const on = selected.has(permission.code);
                        // Turning off is always allowed; turning on only what may be given.
                        const allowed = editable && (on || canTurnOn(permission.code, selected, grantable, index));
                        const blocked = editable && !allowed;
                        const refused = error?.codes.includes(permission.code) ?? false;
                        return (
                          <label
                            key={permission.code}
                            title={blocked ? t("notGrantable") : permission.name}
                            className={cn(
                              "relative inline-flex min-h-11 items-center gap-2 rounded-full border-[1.5px] px-4 py-1.5 text-base leading-snug select-none",
                              "transition-colors duration-150",
                              "has-focus-visible:outline-2 has-focus-visible:outline-offset-2 has-focus-visible:outline-brand has-focus-visible:ring-6 has-focus-visible:ring-accent/40",
                              on ? "border-primary bg-primary text-on-primary" : "border-line bg-surface text-fg",
                              allowed && !on && "cursor-pointer hover:border-brand hover:text-brand",
                              allowed && on && "cursor-pointer hover:bg-primary-strong",
                              !allowed && "cursor-not-allowed",
                              !allowed && !on && "border-dashed border-hairline text-muted",
                              !allowed && on && "opacity-80",
                              refused && "ring-2 ring-danger",
                            )}
                          >
                            <input
                              type="checkbox"
                              className="sr-only"
                              checked={on}
                              disabled={!allowed}
                              aria-describedby={blocked ? `${permission.code}-why` : undefined}
                              onChange={(event) => setSelected((current) => toggle(current, permission.code, event.target.checked, index))}
                            />
                            {on && <CheckIcon className="size-4 shrink-0" />}
                            <span>{labels.action(permission.action)}</span>
                            {permission.requiresMfa && (
                              <>
                                <ShieldIcon className="size-4 shrink-0" aria-hidden />
                                <span className="sr-only">{t("mfa")}</span>
                              </>
                            )}
                            {blocked && (
                              <span id={`${permission.code}-why`} className="sr-only">
                                {t("notGrantable")}
                              </span>
                            )}
                          </label>
                        );
                      })}
                    </div>
                  </div>
                );
              })}
            </div>
          </fieldset>
        ))}
      </div>

      {editable && dirty > 0 && (
        // The save bar stays in reach at the bottom of the screen while there is something to save.
        <div className="sticky bottom-4 z-10 flex flex-wrap items-center justify-between gap-3 rounded-full border border-hairline bg-surface/95 py-2 pr-2 pl-5 shadow-sheet">
          <span className="font-medium text-fg">{t("changesCount", { count: dirty })}</span>
          <span className="flex flex-wrap gap-2">
            <Button variant="ghost" onClick={() => setSelected(new Set(role.permissions))} disabled={saving}>
              {t("discard")}
            </Button>
            <Button onClick={() => void save()} loading={saving}>
              {t("save")}
            </Button>
          </span>
        </div>
      )}
    </section>
  );
}
