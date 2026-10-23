"use client";

import { useFormatter, useLocale, useTranslations } from "next-intl";
import { useCallback, useEffect, useState } from "react";
import { AuthHeading } from "@/components/auth/AuthHeading";
import { FormError } from "@/components/auth/FormError";
import { textLink } from "@/components/auth/links";
import { StatusBadge } from "@/components/onboarding/StatusBadge";
import { useRejectReasonName } from "@/components/onboarding/useRejectReasonName";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { useToast } from "@/components/ui/toast";
import { Link } from "@/i18n/navigation";
import type { OnboardingFailure } from "@/lib/onboarding/api";
import { reviewStage } from "@/lib/onboarding/review";
import { getForReview, getReviewLookups, startReview } from "@/lib/onboarding/review-api";
import type { Lookups, ReviewView } from "@/lib/onboarding/types";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { cn, focusRing } from "@/lib/utils";
import { ApplicationFacts } from "./ApplicationFacts";
import { ChecklistForm } from "./ChecklistForm";
import { DecisionPanel } from "./DecisionPanel";
import { DocumentReview } from "./DocumentReview";
import { QuestionsPanel } from "./QuestionsPanel";
import { useReviewError } from "./useReviewError";

/**
 * One application under review: where it stands and what can be done now, what the applicant
 * said, the documents, the checklist, the questions, the decision and the history. Every change
 * reloads the whole view, so what is shown is always what the API holds.
 */
export function ReviewScreen({ requestNo }: { requestNo: string }) {
  const t = useTranslations("Review");
  const reasonName = useRejectReasonName();
  const format = useFormatter();
  const locale = useLocale();
  const errorMessage = useReviewError();
  const toast = useToast();
  const { can } = usePermissions();

  const [lookups, setLookups] = useState<Lookups | null>(null);
  const [view, setView] = useState<ReviewView | null>(null);
  const [failure, setFailure] = useState<OnboardingFailure | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const reload = useCallback(async () => {
    const result = await getForReview(requestNo);
    if (result.ok) setView(result.value);
    else setFailure(result.failure);
  }, [requestNo]);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      const loaded = await getReviewLookups(locale);
      if (cancelled) return;
      if (!loaded.ok) return setFailure(loaded.failure);
      setLookups(loaded.value);
      await reload();
    })();
    return () => {
      cancelled = true;
    };
  }, [locale, reload]);

  const back = (
    <Link href="/admin/registrations" className={cn(textLink, focusRing, "mb-4")}>
      {t("backToQueue")}
    </Link>
  );

  if (failure) {
    return (
      <div className="grid gap-4">
        <FormError>{failure.code === "registration_not_found" ? t("notFound") : errorMessage(failure)}</FormError>
        {back}
      </div>
    );
  }

  if (!lookups || !view) {
    return (
      <div className="flex justify-center py-16 text-brand">
        <Spinner />
      </div>
    );
  }

  const application = view.application;
  const stage = reviewStage(application.status);
  const canEdit = can("ONBOARDING.REQUESTS.EDIT");
  const reviewing = stage === "review" && canEdit;

  async function start() {
    setBusy(true);
    setError(null);
    const result = await startReview(requestNo);
    setBusy(false);
    if (!result.ok) return setError(errorMessage(result.failure));
    toast({ title: t("detail.started"), variant: "success" });
    await reload();
  }

  const reason =
    application.rejectReason && reasonName(application.rejectReason, view.rejectReasons.find((r) => r.code === application.rejectReason)?.name);

  return (
    <div className="grid gap-8">
      <div className="grid gap-4">
        {back}
        <div className="flex flex-wrap items-start justify-between gap-3">
          <AuthHeading title={application.proposedName} description={t("detail.requestNo", { requestNo: application.requestNo })} focusOnMount />
          <StatusBadge status={application.status} viewer="reviewer" />
        </div>
        <FormError>{error}</FormError>

        <div className="grid gap-3 rounded-cup border border-hairline bg-surface p-4">
          {stage === "start" && (
            <>
              <p>{t("detail.startHelp")}</p>
              {canEdit && (
                <Button loading={busy} onClick={() => void start()} className="justify-self-start">
                  {t("detail.start")}
                </Button>
              )}
            </>
          )}
          {stage === "review" && <p>{t("decision.help")}</p>}
          {stage === "waiting" && <p>{t("detail.waitingHelp")}</p>}
          {application.status === "Approved" && <p>{t("detail.approvedNote", { code: application.institutionCode ?? "" })}</p>}
          {application.status === "Rejected" && (
            <div className="grid gap-1">
              <p>{t("detail.rejectedNote")}</p>
              {reason && <p>{t("detail.reason", { reason })}</p>}
              {application.decisionNote && <p>{t("detail.note", { note: application.decisionNote })}</p>}
            </div>
          )}
          {application.status === "Withdrawn" && <p>{t("detail.withdrawnNote")}</p>}
        </div>
      </div>

      <ApplicationFacts view={application} lookups={lookups} />
      <DocumentReview view={view} canReview={reviewing} onChanged={reload} />
      <ChecklistForm key={`${application.status}:${view.checklist.map((i) => i.checkedAt).join()}`} view={view} canReview={reviewing} onChanged={reload} />
      <QuestionsPanel view={view} canReview={reviewing} onChanged={reload} />
      {stage === "review" && <DecisionPanel view={view} onChanged={reload} />}

      <section className="grid gap-2">
        <h2 className="text-lg font-semibold text-brand">{t("detail.history")}</h2>
        <ol className="grid gap-1 text-sm">
          {application.history.map((change, index) => (
            <li key={index} className="flex flex-wrap justify-between gap-2 border-b border-hairline py-2">
              <StatusBadge status={change.toStatus} viewer="reviewer" />
              <span className="text-muted">{format.dateTime(new Date(change.changedAt), { dateStyle: "medium", timeStyle: "short" })}</span>
            </li>
          ))}
        </ol>
      </section>
    </div>
  );
}
