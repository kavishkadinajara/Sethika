"use client";

import { useTranslations } from "next-intl";
import { DropdownMenu } from "radix-ui";
import { useCallback, useEffect, useState } from "react";
import { FormError } from "@/components/auth/FormError";
import { useDates } from "@/components/bookings/useDates";
import { menuContent, menuItem } from "@/components/shell/menu-styles";
import { initials } from "@/components/shell/useProfile";
import { Button } from "@/components/ui/button";
import { CloseIcon, InboxIcon, KeyIcon, PlusIcon } from "@/components/ui/icons";
import { Spinner } from "@/components/ui/spinner";
import { useToast } from "@/components/ui/toast";
import { assignRole, cancelInvitation, listInvitations, listMembers, listRoles, revokeRole, type AccessResult } from "@/lib/access/api";
import type { InstitutionRole, PendingInvitation, StaffMember } from "@/lib/access/types";
import { colomboToday } from "@/lib/bookings/calendar";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { cn, focusRing } from "@/lib/utils";
import { InviteSheet } from "./InviteSheet";
import { useAccessError } from "./useAccessError";
import { useAccessLabels } from "./useAccessLabels";

type Loaded = { members: StaffMember[]; roles: InstitutionRole[]; invitations: PendingInvitation[] };

/**
 * The institution's staff and their roles (plan 04, U3), the invitations still waiting, and the
 * way to invite someone new. A role chip can be taken away, and a role added, only where the
 * caller's rank allows it (`canManage`) and never on their own row; the API checks both again,
 * and every change needs a step-up, which apiFetch asks for.
 */
export function MembersScreen() {
  const t = useTranslations("Access.members");
  const tInvite = useTranslations("Access.invite");
  const tRoles = useTranslations("Access.roles");
  const labels = useAccessLabels();
  const errorMessage = useAccessError();
  const dates = useDates();
  const toast = useToast();
  const { can, userId } = usePermissions();
  const mayEdit = can("ACCESS.MEMBERS.EDIT");

  const [loaded, setLoaded] = useState<Loaded | null>(null);
  const [failure, setFailure] = useState<string | null>(null);
  const [inviting, setInviting] = useState(false);
  const [busy, setBusy] = useState<string | null>(null);

  const load = useCallback(async () => {
    const [members, roles, invitations] = await Promise.all([listMembers(), listRoles(), listInvitations()]);
    if (!members.ok) return setFailure(errorMessage(members.failure));
    // Seeing the staff does not need seeing the roles; without them nothing can be handed out.
    setLoaded({
      members: members.value,
      roles: roles.ok ? roles.value : [],
      invitations: invitations.ok ? invitations.value : [],
    });
  }, [errorMessage]);

  useEffect(() => {
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

  const manageable = loaded.roles.filter((r) => r.canManage);
  const roleOf = (code: string) => loaded.roles.find((r) => r.code === code);
  const roleName = (code: string, name: string) => {
    const role = roleOf(code);
    return role ? labels.role(role) : name;
  };

  /** One change to the staff, with a toast either way and the lists read again after. */
  async function change(key: string, run: () => Promise<AccessResult>, done: string) {
    setBusy(key);
    const result = await run();
    setBusy(null);
    if (!result.ok) {
      toast({ variant: "error", title: errorMessage(result.failure) });
      return;
    }
    toast({ variant: "success", title: done });
    await load();
  }

  return (
    <div className="grid gap-10">
      {mayEdit && manageable.length > 0 && (
        <div>
          <Button onClick={() => setInviting(true)}>
            <PlusIcon className="size-5" />
            {t("invite")}
          </Button>
        </div>
      )}

      {loaded.invitations.length > 0 && (
        <section aria-labelledby="pending" className="grid gap-4">
          <h2 id="pending" className="text-xl font-semibold text-brand">
            {t("pending")}
          </h2>
          <ul className="grid gap-3 lg:grid-cols-2 2xl:grid-cols-3">
            {loaded.invitations.map((invitation) => (
              <li key={invitation.id} className="grid gap-3 rounded-bowl border border-dashed border-line bg-surface p-4">
                <div className="flex items-start gap-3">
                  <span className="grid size-10 shrink-0 place-items-center rounded-full bg-subtle text-brand">
                    <InboxIcon className="size-5" />
                  </span>
                  <div className="grid min-w-0 gap-0.5">
                    <p className="font-semibold text-fg">{invitation.name}</p>
                    <p className="text-sm text-muted">
                      {invitation.phone} · {roleName(invitation.roleCode, invitation.roleName)}
                    </p>
                    <p className="text-sm text-muted">
                      {t("invitedBy", { name: invitation.invitedBy })} · {t("expires", { date: dates.medium(colomboToday(new Date(invitation.expiresAt))) })}
                    </p>
                  </div>
                </div>
                {mayEdit && roleOf(invitation.roleCode)?.canManage && (
                  <Button
                    variant="ghost"
                    className="justify-self-start text-danger hover:bg-danger/10"
                    loading={busy === invitation.id}
                    onClick={() =>
                      void change(invitation.id, () => cancelInvitation(invitation.id), t("cancelled", { name: invitation.name }))
                    }
                  >
                    {t("cancel")}
                  </Button>
                )}
              </li>
            ))}
          </ul>
        </section>
      )}

      <section aria-labelledby="staff" className="grid gap-4">
        <h2 id="staff" className="text-xl font-semibold text-brand">
          {t("staffList")}
        </h2>
        {loaded.members.length === 0 ? (
          <p className="text-muted">{t("empty")}</p>
        ) : (
          <ul className="grid gap-3 lg:grid-cols-2 2xl:grid-cols-3">
            {loaded.members.map((member) => {
              const isMe = member.userId === userId;
              const held = new Set(member.roles.map((r) => r.code));
              const addable = manageable.filter((r) => !held.has(r.code));
              return (
                <li key={member.userId} className="grid gap-3 rounded-bowl border border-hairline bg-surface p-4 shadow-lift">
                  <div className="flex items-center gap-3">
                    <span aria-hidden className="grid size-11 shrink-0 place-items-center rounded-full bg-estate font-semibold text-ivory">
                      {initials(member.fullName)}
                    </span>
                    <div className="grid min-w-0">
                      <p className="flex flex-wrap items-center gap-2 font-semibold text-fg">
                        <span className="truncate">{member.fullName}</span>
                        {isMe && <span className="rounded-full bg-accent/25 px-2 py-0.5 text-xs font-semibold text-fg">{t("you")}</span>}
                      </p>
                      {member.phone && <p className="text-sm text-muted tabular-nums">{member.phone}</p>}
                    </div>
                  </div>

                  <ul className="flex flex-wrap gap-2" aria-label={tRoles("title")}>
                    {member.roles.map((r) => {
                      const name = roleName(r.code, r.name);
                      const removable = mayEdit && !isMe && (roleOf(r.code)?.canManage ?? false);
                      return (
                        <li key={r.code} className="inline-flex min-h-9 items-center gap-1.5 rounded-full bg-primary/10 py-1 pr-1 pl-3 text-sm font-medium text-brand">
                          <KeyIcon className="size-4 shrink-0" />
                          {name}
                          {removable ? (
                            <button
                              type="button"
                              aria-label={t("removeRole", { role: name, name: member.fullName })}
                              disabled={busy !== null}
                              onClick={() =>
                                void change(`${member.userId}:${r.code}`, () => revokeRole(r.code, member.userId), t("removed", { role: name, name: member.fullName }))
                              }
                              className={cn("grid size-7 place-items-center rounded-full hover:bg-danger/15 hover:text-danger", focusRing)}
                            >
                              {busy === `${member.userId}:${r.code}` ? <Spinner size="sm" decorative /> : <CloseIcon className="size-4" />}
                            </button>
                          ) : (
                            <span className="w-2" />
                          )}
                        </li>
                      );
                    })}
                  </ul>

                  {mayEdit && !isMe && addable.length > 0 && (
                    <DropdownMenu.Root>
                      <DropdownMenu.Trigger asChild>
                        <Button variant="ghost" className="justify-self-start" disabled={busy !== null}>
                          <PlusIcon className="size-5" />
                          {t("addRole")}
                        </Button>
                      </DropdownMenu.Trigger>
                      <DropdownMenu.Portal>
                        <DropdownMenu.Content align="start" sideOffset={6} collisionPadding={16} className={menuContent}>
                          {addable.map((r) => (
                            <DropdownMenu.Item
                              key={r.code}
                              className={menuItem}
                              onSelect={() =>
                                void change(`${member.userId}:+${r.code}`, () => assignRole(r.code, member.userId), t("added", { role: labels.role(r), name: member.fullName }))
                              }
                            >
                              <KeyIcon className="size-5 shrink-0 text-muted" />
                              <span className="flex-1">{labels.role(r)}</span>
                              <span className="text-sm text-muted">{labels.level(r.level, r.levelName)}</span>
                            </DropdownMenu.Item>
                          ))}
                        </DropdownMenu.Content>
                      </DropdownMenu.Portal>
                    </DropdownMenu.Root>
                  )}
                </li>
              );
            })}
          </ul>
        )}
      </section>

      {inviting && (
        <InviteSheet
          roles={manageable}
          onClose={() => setInviting(false)}
          onInvited={(phone) => {
            setInviting(false);
            toast({ variant: "success", title: tInvite("sent", { phone }) });
            void load();
          }}
        />
      )}
    </div>
  );
}
