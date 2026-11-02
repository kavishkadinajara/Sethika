"use client";

import { useLocale, useTranslations } from "next-intl";
import { useCallback, useEffect, useState, type FormEvent } from "react";
import { FormError } from "@/components/auth/FormError";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { LinkIcon } from "@/components/ui/icons";
import { Input } from "@/components/ui/input";
import { Sheet, SheetBody, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { useToast } from "@/components/ui/toast";
import { invite, listInvitations, withdrawInvitation, type BookingFailure } from "@/lib/bookings/api";
import { localePath } from "@/lib/bookings/seo";
import type { DonorBooking, Invitation } from "@/lib/bookings/types";
import { cn } from "@/lib/utils";
import { useBookingError } from "./useBookingError";
import { useWide } from "./useWide";

/** BookingContributor.MaxName and MaxNote, and the cap per booking (H16). */
const MAX_NAME = 150;
const MAX_NOTE = 200;
const MAX_INVITATIONS = 10;

/**
 * "Share this dana" (plan 07, H16-H19): the donor names someone they want to share the booking with
 * and Sethika makes a link, which the donor sends them - Sethika texts nobody. The link is shown once,
 * at the moment it is made: only its hash is kept. Below, the people invited, who joined or
 * declined, and a withdraw for an invitation nobody has answered.
 */
export function ShareSheet({ booking, onClose }: { booking: DonorBooking; onClose: () => void }) {
  const t = useTranslations("MyBookings.share");
  const wide = useWide();
  const errorMessage = useBookingError();
  const [invitations, setInvitations] = useState<Invitation[] | null>(null);
  const [loadFailure, setLoadFailure] = useState<BookingFailure | null>(null);
  const [link, setLink] = useState<{ name: string; url: string } | null>(null);

  const load = useCallback(async () => {
    setLoadFailure(null);
    const result = await listInvitations(booking.bookingNo);
    if (result.ok) setInvitations(result.value);
    else setLoadFailure(result.failure);
  }, [booking.bookingNo]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load]);

  return (
    <Sheet open onOpenChange={(open) => !open && onClose()}>
      <SheetContent side={wide ? "right" : "bottom"}>
        <SheetHeader>
          <SheetTitle>{t("title")}</SheetTitle>
          <SheetDescription>{t("description")}</SheetDescription>
        </SheetHeader>
        <SheetBody className="grid gap-6">
          {link ? (
            <MadeLink name={link.name} url={link.url} onAnother={() => setLink(null)} />
          ) : (
            invitations !== null &&
            (invitations.length >= MAX_INVITATIONS ? (
              <p className="rounded-cup bg-subtle px-4 py-3 text-sm text-fg">{t("full", { max: MAX_INVITATIONS })}</p>
            ) : (
              <InviteForm
                bookingNo={booking.bookingNo}
                onMade={(name, url) => {
                  setLink({ name, url });
                  void load();
                }}
              />
            ))
          )}

          <section className="grid gap-3" aria-labelledby="invited">
            <h3 id="invited" className="text-lg font-semibold text-brand">
              {t("invited")}
            </h3>
            {loadFailure ? (
              <FormError>{errorMessage(loadFailure)}</FormError>
            ) : invitations === null ? (
              <div className="flex justify-center py-4 text-brand">
                <Spinner />
              </div>
            ) : invitations.length === 0 ? (
              <p className="text-muted">{t("nobody")}</p>
            ) : (
              <ul className="grid gap-2">
                {invitations.map((invitation) => (
                  <InvitationRow key={invitation.publicId} bookingNo={booking.bookingNo} invitation={invitation} onChanged={() => void load()} />
                ))}
              </ul>
            )}
          </section>
        </SheetBody>
        <SheetFooter>
          <Button variant="ghost" onClick={onClose}>
            {t("close")}
          </Button>
        </SheetFooter>
      </SheetContent>
    </Sheet>
  );
}

function InviteForm({ bookingNo, onMade }: { bookingNo: string; onMade: (name: string, url: string) => void }) {
  const t = useTranslations("MyBookings.share");
  const locale = useLocale();
  const errorMessage = useBookingError();
  const [name, setName] = useState("");
  const [note, setNote] = useState("");
  const [nameError, setNameError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [failure, setFailure] = useState<BookingFailure | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setFailure(null);
    if (!name.trim()) {
      setNameError(t("nameRequired"));
      return;
    }
    setSubmitting(true);
    const result = await invite(bookingNo, name.trim(), note.trim() || null);
    setSubmitting(false);
    if (result.ok) {
      onMade(name.trim(), `${window.location.origin}${localePath(locale, `/share/${result.value.token}`)}`);
      setName("");
      setNote("");
    } else {
      setFailure(result.failure);
    }
  }

  return (
    <form onSubmit={submit} noValidate className="grid gap-4">
      <Field label={t("name")} description={t("nameHint")} error={nameError} required>
        <Input
          value={name}
          onChange={(event) => {
            setName(event.target.value);
            setNameError(null);
          }}
          maxLength={MAX_NAME}
          autoComplete="off"
        />
      </Field>
      <Field label={t("note")} description={t("noteHint")}>
        <Input value={note} onChange={(event) => setNote(event.target.value)} maxLength={MAX_NOTE} autoComplete="off" />
      </Field>
      <FormError>{failure && errorMessage(failure)}</FormError>
      <Button type="submit" loading={submitting} className="justify-self-start">
        <LinkIcon className="size-5" />
        {t("make")}
      </Button>
    </form>
  );
}

/** The link just made: copy it, or hand it to the phone's own share menu. */
function MadeLink({ name, url, onAnother }: { name: string; url: string; onAnother: () => void }) {
  const t = useTranslations("MyBookings.share");
  const toast = useToast();
  const canShare = typeof navigator !== "undefined" && typeof navigator.share === "function";

  async function copy() {
    try {
      await navigator.clipboard.writeText(url);
      toast({ variant: "success", title: t("copied") });
    } catch {
      toast({ variant: "error", title: t("copyFailed") });
    }
  }

  return (
    <div className="grid gap-3 rounded-cup border border-brand/30 bg-brand/6 p-4" aria-live="polite">
      <p className="font-medium text-fg">{t("made", { name })}</p>
      <Input readOnly value={url} aria-label={t("linkLabel", { name })} onFocus={(event) => event.target.select()} className="text-sm" />
      <p className="text-sm text-muted">{t("onceOnly")}</p>
      <div className="flex flex-wrap gap-2">
        <Button onClick={() => void copy()}>{t("copy")}</Button>
        {canShare && (
          <Button variant="outline" onClick={() => void navigator.share({ text: t("shareText"), url }).catch(() => undefined)}>
            {t("shareVia")}
          </Button>
        )}
        <Button variant="ghost" onClick={onAnother}>
          {t("another")}
        </Button>
      </div>
    </div>
  );
}

function InvitationRow({ bookingNo, invitation, onChanged }: { bookingNo: string; invitation: Invitation; onChanged: () => void }) {
  const t = useTranslations("MyBookings.share");
  const toast = useToast();
  const errorMessage = useBookingError();
  const [busy, setBusy] = useState(false);

  async function withdraw() {
    setBusy(true);
    const result = await withdrawInvitation(bookingNo, invitation.publicId);
    setBusy(false);
    if (result.ok) toast({ variant: "success", title: t("withdrawn", { name: invitation.name }) });
    else toast({ variant: "error", title: errorMessage(result.failure) });
    onChanged();
  }

  return (
    <li className="flex flex-wrap items-center justify-between gap-2 rounded-cup border border-hairline px-3 py-2">
      <span className="grid min-w-0 gap-0.5">
        <span className="font-medium text-fg">{invitation.name}</span>
        {invitation.note && <span className="text-sm text-muted">{invitation.note}</span>}
      </span>
      <span className="flex items-center gap-2">
        <span
          className={cn(
            "rounded-full px-2.5 py-0.5 text-sm font-medium",
            invitation.status === "Joined" ? "bg-success-soft text-success" : invitation.status === "Declined" ? "bg-subtle text-muted" : "bg-accent/15 text-fg",
          )}
        >
          {t(`status.${invitation.status}`)}
        </span>
        {invitation.status === "Invited" && (
          <Button variant="ghost" loading={busy} onClick={() => void withdraw()} className="text-danger hover:bg-danger/10">
            {t("withdraw")}
          </Button>
        )}
      </span>
    </li>
  );
}
