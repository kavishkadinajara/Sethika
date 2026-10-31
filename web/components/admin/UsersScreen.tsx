"use client";

import { useTranslations } from "next-intl";
import { DropdownMenu } from "radix-ui";
import { useCallback, useEffect, useState } from "react";
import { FormError } from "@/components/auth/FormError";
import { menuContent, menuItem } from "@/components/shell/menu-styles";
import { initials } from "@/components/shell/useProfile";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { CloseIcon, InstitutionIcon, PlusIcon, ShieldIcon } from "@/components/ui/icons";
import { Input } from "@/components/ui/input";
import { Spinner } from "@/components/ui/spinner";
import { useToast } from "@/components/ui/toast";
import { grantPlatformRole, listPlatformRoles, listUsers, revokePlatformRole, setUserSuspended, type AdminResult } from "@/lib/admin/api";
import type { AdminUser, PlatformRole } from "@/lib/admin/types";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { cn, focusRing } from "@/lib/utils";
import { CreateUserSheet } from "./CreateUserSheet";
import { useAdminError } from "./useAdminError";

const statusTone: Record<AdminUser["status"], string> = {
  Active: "bg-success-soft text-success",
  PendingVerification: "bg-accent/20 text-fg",
  Suspended: "bg-danger-soft text-danger",
  Deactivated: "bg-subtle text-muted",
};

/**
 * Every account on Sethika (plan 04, Y5): who they are, where they work and with which roles,
 * their platform roles; making an account for someone who cannot sign up (Y6), suspending one,
 * and giving or taking platform roles below the caller's own rank. Every write needs a step-up,
 * which apiFetch asks for; none is offered on the caller's own row.
 */
export function UsersScreen() {
  const t = useTranslations("Admin.users");
  const tCreate = useTranslations("Admin.create");
  const errorMessage = useAdminError();
  const toast = useToast();
  const { can, userId } = usePermissions();
  const mayEdit = can("PLATFORM.USERS.EDIT");

  const [search, setSearch] = useState("");
  const [users, setUsers] = useState<AdminUser[] | null>(null);
  const [roles, setRoles] = useState<PlatformRole[]>([]);
  const [failure, setFailure] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [busy, setBusy] = useState<string | null>(null);

  const load = useCallback(
    async (term: string) => {
      const result = await listUsers(term);
      if (result.ok) {
        setUsers(result.value);
        setFailure(null);
      } else {
        setFailure(errorMessage(result.failure));
      }
    },
    [errorMessage],
  );

  useEffect(() => {
    const timer = setTimeout(() => void load(search.trim()), 250);
    return () => clearTimeout(timer);
  }, [search, load]);

  useEffect(() => {
    void listPlatformRoles().then((result) => {
      if (result.ok) setRoles(result.value);
    });
  }, []);

  async function change(key: string, run: () => Promise<AdminResult>, done: string) {
    setBusy(key);
    const result = await run();
    setBusy(null);
    if (!result.ok) {
      toast({ variant: "error", title: errorMessage(result.failure) });
      return;
    }
    toast({ variant: "success", title: done });
    await load(search.trim());
  }

  const roleName = (code: string) => roles.find((r) => r.code === code)?.name ?? code;

  return (
    <div className="grid gap-6">
      <div className="flex flex-wrap items-end gap-4">
        <div className="w-full max-w-xl">
          <Field label={t("search")} description={t("searchHint")}>
            <Input type="search" value={search} onChange={(event) => setSearch(event.target.value)} autoComplete="off" />
          </Field>
        </div>
        {can("PLATFORM.USERS.CREATE") && (
          <Button onClick={() => setCreating(true)}>
            <PlusIcon className="size-5" />
            {t("create")}
          </Button>
        )}
      </div>

      <FormError>{failure}</FormError>

      {users === null ? (
        <div className="flex justify-center py-16 text-brand">
          <Spinner />
        </div>
      ) : users.length === 0 ? (
        <p className="text-muted">{t("empty")}</p>
      ) : (
        <ul aria-label={t("list")} className="grid gap-3 lg:grid-cols-2 2xl:grid-cols-3">
          {users.map((user) => {
            const isMe = user.id === userId;
            const addable = roles.filter((r) => r.canManage && !user.platformRoles.includes(r.code));
            return (
              <li key={user.id} className="grid content-start gap-3 rounded-bowl border border-hairline bg-surface p-5 shadow-lift">
                <div className="flex items-start gap-3">
                  <span aria-hidden className="grid size-11 shrink-0 place-items-center rounded-full bg-primary font-semibold text-on-primary">
                    {initials(user.fullName)}
                  </span>
                  <div className="grid min-w-0 flex-1 gap-0.5">
                    <p className="flex flex-wrap items-center gap-2">
                      <span className="font-semibold text-fg">{user.fullName}</span>
                      {isMe && <span className="rounded-full bg-accent/25 px-2 py-0.5 text-xs font-semibold text-fg">{t("you")}</span>}
                      <span className={cn("rounded-full px-2.5 py-0.5 text-xs font-semibold", statusTone[user.status])}>{t(`status.${user.status}`)}</span>
                    </p>
                    <p className="text-sm text-muted">
                      {[user.phone, t(`type.${user.userType}`)].filter(Boolean).join(" · ")}
                    </p>
                  </div>
                </div>

                {user.institutions.length > 0 && (
                  <ul aria-label={t("institutions")} className="grid gap-1 text-sm">
                    {user.institutions.map((institution) => (
                      <li key={institution.slug} className="flex items-start gap-2 text-fg">
                        <InstitutionIcon className="mt-0.5 size-4 shrink-0 text-muted" />
                        <span>
                          {institution.name} <span className="text-muted">· {institution.roles.join(", ")}</span>
                        </span>
                      </li>
                    ))}
                  </ul>
                )}

                {user.platformRoles.length > 0 && (
                  <ul aria-label={t("platformRoles")} className="flex flex-wrap gap-2">
                    {user.platformRoles.map((code) => {
                      const removable = mayEdit && !isMe && (roles.find((r) => r.code === code)?.canManage ?? false);
                      return (
                        <li key={code} className="inline-flex min-h-9 items-center gap-1.5 rounded-full bg-primary/10 py-1 pr-1 pl-3 text-sm font-medium text-brand">
                          <ShieldIcon className="size-4 shrink-0" />
                          {roleName(code)}
                          {removable ? (
                            <button
                              type="button"
                              aria-label={t("removePlatformRole", { role: roleName(code), name: user.fullName })}
                              disabled={busy !== null}
                              onClick={() =>
                                void change(`${user.id}:-${code}`, () => revokePlatformRole(user.id, code), t("revoked", { role: roleName(code), name: user.fullName }))
                              }
                              className={cn("grid size-7 place-items-center rounded-full hover:bg-danger/15 hover:text-danger", focusRing)}
                            >
                              <CloseIcon className="size-4" />
                            </button>
                          ) : (
                            <span className="w-2" />
                          )}
                        </li>
                      );
                    })}
                  </ul>
                )}

                {mayEdit && !isMe && (
                  <div className="flex flex-wrap gap-2">
                    {addable.length > 0 && (
                      <DropdownMenu.Root>
                        <DropdownMenu.Trigger asChild>
                          <Button variant="ghost" disabled={busy !== null}>
                            <PlusIcon className="size-5" />
                            {t("addPlatformRole")}
                          </Button>
                        </DropdownMenu.Trigger>
                        <DropdownMenu.Portal>
                          <DropdownMenu.Content align="start" sideOffset={6} collisionPadding={16} className={menuContent}>
                            {addable.map((r) => (
                              <DropdownMenu.Item
                                key={r.code}
                                className={menuItem}
                                onSelect={() =>
                                  void change(`${user.id}:+${r.code}`, () => grantPlatformRole(user.id, r.code), t("granted", { role: r.name, name: user.fullName }))
                                }
                              >
                                <ShieldIcon className="size-5 shrink-0 text-muted" />
                                {r.name}
                              </DropdownMenu.Item>
                            ))}
                          </DropdownMenu.Content>
                        </DropdownMenu.Portal>
                      </DropdownMenu.Root>
                    )}
                    {user.status === "Suspended" ? (
                      <Button
                        variant="ghost"
                        loading={busy === `${user.id}:status`}
                        disabled={busy !== null}
                        onClick={() => void change(`${user.id}:status`, () => setUserSuspended(user.id, false), t("reactivated", { name: user.fullName }))}
                      >
                        {t("reactivate")}
                      </Button>
                    ) : (
                      <Button
                        variant="ghost"
                        className="text-danger hover:bg-danger/10"
                        loading={busy === `${user.id}:status`}
                        disabled={busy !== null}
                        onClick={() => void change(`${user.id}:status`, () => setUserSuspended(user.id, true), t("suspended", { name: user.fullName }))}
                      >
                        {t("suspend")}
                      </Button>
                    )}
                  </div>
                )}
              </li>
            );
          })}
        </ul>
      )}

      {creating && (
        <CreateUserSheet
          onClose={() => setCreating(false)}
          onCreated={(name) => {
            setCreating(false);
            toast({ variant: "success", title: tCreate("created", { name }) });
            void load(search.trim());
          }}
        />
      )}
    </div>
  );
}
