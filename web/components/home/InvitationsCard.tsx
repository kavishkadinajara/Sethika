"use client";

import { useTranslations } from "next-intl";
import { useEffect, useState } from "react";
import { useAccessError } from "@/components/access/useAccessError";
import { useDates } from "@/components/bookings/useDates";
import { Button } from "@/components/ui/button";
import { InboxIcon, InstitutionIcon } from "@/components/ui/icons";
import { useToast } from "@/components/ui/toast";
import { useRouter } from "@/i18n/navigation";
import { answerInvitation, myInvitations } from "@/lib/access/api";
import type { MyInvitation } from "@/lib/access/types";
import { session } from "@/lib/auth";
import { colomboToday } from "@/lib/bookings/calendar";
import { markShellStale } from "@/lib/shell/summary";

/**
 * Invitations onto an institution's staff waiting for this person (plan 04, X1-X4), at the top of
 * their dashboard whatever session they are in. Accepting makes the role theirs at once; the card
 * then offers to open the institution, which rotates the session into it.
 */
export function InvitationsCard() {
  const t = useTranslations("Access.invitations");
  const tErrors = useTranslations("Errors");
  const errorMessage = useAccessError();
  const dates = useDates();
  const toast = useToast();
  const router = useRouter();
  const [invitations, setInvitations] = useState<MyInvitation[]>([]);
  const [busy, setBusy] = useState<string | null>(null);
  const [joined, setJoined] = useState<MyInvitation | null>(null);

  useEffect(() => {
    let cancelled = false;
    void myInvitations().then((result) => {
      if (!cancelled && result.ok) setInvitations(result.value);
    });
    return () => {
      cancelled = true;
    };
  }, []);

  async function answer(invitation: MyInvitation, choice: "accept" | "decline") {
    setBusy(`${invitation.id}:${choice}`);
    const result = await answerInvitation(invitation.id, choice);
    setBusy(null);
    if (!result.ok) {
      toast({ variant: "error", title: errorMessage(result.failure) });
      return;
    }
    setInvitations((current) => current.filter((i) => i.id !== invitation.id));
    markShellStale();
    if (choice === "accept") {
      setJoined(invitation);
      toast({ variant: "success", title: t("accepted", { institution: invitation.institution }) });
    } else {
      toast({ title: t("declined") });
    }
  }

  async function open(invitation: MyInvitation) {
    setBusy("open");
    const result = await session.switchInstitution(invitation.slug);
    setBusy(null);
    if (result.ok) {
      setJoined(null);
      router.push("/");
    } else {
      toast({ variant: "error", title: result.reason === "not_available" ? tErrors("institution_not_available") : tErrors("network") });
    }
  }

  if (invitations.length === 0 && !joined) return null;

  return (
    <section aria-labelledby="invitations" className="grid gap-4">
      <h2 id="invitations" className="text-2xl font-semibold text-brand">
        {t("title")}
      </h2>
      <ul className="grid gap-4 lg:grid-cols-2">
        {invitations.map((invitation) => (
          <li key={invitation.id} className="relative grid gap-4 rounded-bowl border border-hairline bg-surface p-5 shadow-lift">
            <div className="flex items-start gap-4">
              <span className="grid size-12 shrink-0 place-items-center rounded-2xl bg-accent/20 text-brand">
                <InboxIcon className="size-6" />
              </span>
              <div className="grid gap-1">
                <p className="text-lg leading-snug text-fg">
                  {t("body", { by: invitation.invitedBy, institution: invitation.institution, role: invitation.roleName })}
                </p>
                <p className="text-sm text-muted">{t("expires", { date: dates.medium(colomboToday(new Date(invitation.expiresAt))) })}</p>
              </div>
            </div>
            <div className="flex flex-wrap gap-3">
              <Button loading={busy === `${invitation.id}:accept`} disabled={busy !== null} onClick={() => void answer(invitation, "accept")}>
                {t("accept")}
              </Button>
              <Button variant="ghost" loading={busy === `${invitation.id}:decline`} disabled={busy !== null} onClick={() => void answer(invitation, "decline")}>
                {t("decline")}
              </Button>
            </div>
          </li>
        ))}
        {joined && (
          <li className="grid gap-4 rounded-bowl border border-primary bg-primary/8 p-5">
            <p className="text-lg text-fg">{t("accepted", { institution: joined.institution })}</p>
            <Button className="justify-self-start" loading={busy === "open"} onClick={() => void open(joined)}>
              <InstitutionIcon className="size-5" />
              {t("open", { institution: joined.institution })}
            </Button>
          </li>
        )}
      </ul>
    </section>
  );
}
