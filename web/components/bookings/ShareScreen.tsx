"use client";

import { useLocale, useTranslations } from "next-intl";
import { useCallback, useEffect, useState } from "react";
import { FormError } from "@/components/auth/FormError";
import { PersonalSwitch } from "@/components/shell/PersonalSwitch";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { CheckCircleIcon } from "@/components/ui/icons";
import { Spinner } from "@/components/ui/spinner";
import { Link } from "@/i18n/navigation";
import { answerShare, openShare, type BookingFailure } from "@/lib/bookings/api";
import { inLocale } from "@/lib/bookings/names";
import type { ShareSummary } from "@/lib/bookings/types";
import { useSession } from "@/lib/auth/useSession";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { EmptyState } from "./EmptyState";
import { useBookingError } from "./useBookingError";
import { useDates } from "./useDates";

/**
 * A co-sponsor's link (plan 07, H17): the booking's public summary - where, which meal, when, whose
 * dana and in whose memory - and, while the booking and the invitation are open, join or decline.
 * Anyone may look; answering asks for a sign-in first and comes back here.
 */
export function ShareScreen({ token }: { token: string }) {
  const t = useTranslations("Share");
  const locale = useLocale();
  const dates = useDates();
  const errorMessage = useBookingError();
  const [summary, setSummary] = useState<ShareSummary | null>(null);
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [answered, setAnswered] = useState<"joined" | "declined" | null>(null);

  const load = useCallback(async () => {
    setFailure(null);
    const result = await openShare(token);
    if (result.ok) setSummary(result.value);
    else setFailure(result.failure);
  }, [token]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load]);

  if (failure) {
    return failure.status === 404 ? <EmptyState>{t("notFound")}</EmptyState> : <FormError>{errorMessage(failure)}</FormError>;
  }

  if (!summary) {
    return (
      <div className="flex justify-center py-16 text-brand">
        <Spinner />
      </div>
    );
  }

  const institution = inLocale(locale, summary.institutionName, summary.institutionNameSi, summary.institutionNameTa);
  const meal = inLocale(locale, summary.mealName, summary.mealNameSi, summary.mealNameTa);

  return (
    <Card accent className="mx-auto grid w-full max-w-xl gap-5 p-5 sm:p-8">
      <header className="grid gap-2">
        <p className="text-sm font-medium text-muted">{t("invitedBy", { name: summary.donorName })}</p>
        <h1 className="text-3xl font-semibold leading-tight text-brand">{t("title")}</h1>
      </header>

      <dl className="grid gap-3 text-fg">
        <div className="grid gap-0.5">
          <dt className="text-sm text-muted">{t("where")}</dt>
          <dd>
            <Link href={`/i/${summary.institution}`} className="font-medium text-brand underline-offset-4 hover:underline">
              {institution}
            </Link>
          </dd>
        </div>
        <div className="grid gap-0.5">
          <dt className="text-sm text-muted">{t("when")}</dt>
          <dd className="font-medium">
            {meal} · {dates.full(summary.date)} · <span className="tabular-nums">{summary.startTime}</span>
          </dd>
        </div>
        {summary.inMemoryOf && (
          <div className="grid gap-0.5">
            <dt className="text-sm text-muted">{t("inMemoryOfLabel")}</dt>
            <dd className="font-tagline text-lg italic">{summary.inMemoryOf}</dd>
          </div>
        )}
      </dl>

      {answered ? (
        <div className="grid justify-items-start gap-3" aria-live="polite">
          {answered === "joined" && <CheckCircleIcon className="size-10 text-success" />}
          <p className="text-lg font-semibold text-fg">{answered === "joined" ? t("joined") : t("declined")}</p>
          {answered === "joined" && (
            <>
              <p className="text-muted">{t("joinedNext")}</p>
              <Button asChild>
                <Link href="/bookings/mine">{t("seeMine")}</Link>
              </Button>
            </>
          )}
        </div>
      ) : summary.booking !== "Open" ? (
        <p className="rounded-cup bg-subtle px-4 py-3 text-fg">{t(`booking.${summary.booking}`)}</p>
      ) : summary.invitation !== "Invited" ? (
        <p className="rounded-cup bg-subtle px-4 py-3 text-fg">{t(`invitation.${summary.invitation}`)}</p>
      ) : (
        <Answer token={token} onAnswered={setAnswered} onStale={() => void load()} />
      )}
    </Card>
  );
}

/** Join or decline, once signed in to a personal session (H36); else how to get one. */
function Answer({ token, onAnswered, onStale }: { token: string; onAnswered: (answer: "joined" | "declined") => void; onStale: () => void }) {
  const t = useTranslations("Share");
  const session = useSession();
  const { can, isLoading } = usePermissions();
  const errorMessage = useBookingError();
  const [busy, setBusy] = useState<"join" | "decline" | null>(null);
  const [failure, setFailure] = useState<BookingFailure | null>(null);

  if (session.status === "loading" || (session.status === "authenticated" && isLoading)) {
    return (
      <div className="flex justify-center py-6 text-brand">
        <Spinner />
      </div>
    );
  }

  if (session.status !== "authenticated") {
    const next = `?next=${encodeURIComponent(`/share/${token}`)}`;
    return (
      <div className="grid gap-4">
        <p className="text-fg">{t("signInFirst")}</p>
        <div className="flex flex-wrap gap-3">
          <Button asChild>
            <Link href={`/login${next}`}>{t("signIn")}</Link>
          </Button>
          <Button asChild variant="outline">
            <Link href={`/signup${next}`}>{t("createAccount")}</Link>
          </Button>
        </div>
      </div>
    );
  }

  if (session.activeInstitution) {
    return (
      <div className="grid gap-4 text-fg">
        <p>{t("institutionSession")}</p>
        <PersonalSwitch />
      </div>
    );
  }

  if (!can("MYBOOKINGS.BOOKINGS.EDIT")) return <p className="text-fg">{t("notAllowed")}</p>;

  async function answer(join: boolean) {
    setBusy(join ? "join" : "decline");
    setFailure(null);
    const result = await answerShare(token, join);
    setBusy(null);
    if (result.ok) {
      onAnswered(join ? "joined" : "declined");
      return;
    }
    setFailure(result.failure);
    // Answered or closed by someone else meanwhile: show the link as it is now.
    if (result.failure.code === "invitation_answered" || result.failure.code === "booking_closed") onStale();
  }

  return (
    <div className="grid gap-3">
      <p className="text-fg">{t("ask")}</p>
      <div className="flex flex-wrap gap-3">
        <Button size="lg" loading={busy === "join"} disabled={busy !== null} onClick={() => void answer(true)}>
          {t("join")}
        </Button>
        <Button size="lg" variant="ghost" loading={busy === "decline"} disabled={busy !== null} onClick={() => void answer(false)}>
          {t("decline")}
        </Button>
      </div>
      <FormError>{failure && errorMessage(failure)}</FormError>
    </div>
  );
}
