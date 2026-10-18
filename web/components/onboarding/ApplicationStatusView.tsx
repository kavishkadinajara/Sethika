"use client";

import { useFormatter, useTranslations } from "next-intl";
import { useState, type FormEvent, type ReactNode } from "react";
import { AuthHeading } from "@/components/auth/AuthHeading";
import { FormError } from "@/components/auth/FormError";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { inputClasses } from "@/components/ui/input";
import { useRouter } from "@/i18n/navigation";
import { session } from "@/lib/auth";
import { answerQuestions } from "@/lib/onboarding/api";
import { canWithdraw } from "@/lib/onboarding/progress";
import type { ApplicationView, Lookups } from "@/lib/onboarding/types";
import { cn } from "@/lib/utils";
import { DocumentsStep } from "./DocumentsStep";
import { StatusBadge } from "./StatusBadge";
import { useRejectReasonName } from "./useRejectReasonName";
import { useOnboardingError } from "./useOnboardingError";
import { WithdrawControl } from "./WithdrawControl";

/**
 * A submitted application: where it stands, the reviewer's questions and the answers to them, the
 * documents, withdrawal while it is still possible, and the way into the institution once approved.
 */
export function ApplicationStatusView({
  view,
  lookups,
  onChanged,
  onEdit,
}: {
  view: ApplicationView;
  lookups: Lookups;
  onChanged: () => Promise<void>;
  onEdit: () => void;
}) {
  const t = useTranslations("Apply");
  const format = useFormatter();
  const errorMessage = useOnboardingError();
  const router = useRouter();
  const reasonName = useRejectReasonName();
  const [formError, setFormError] = useState<ReactNode>(null);
  const [busy, setBusy] = useState(false);

  const open = view.questions.filter((q) => q.response === null);
  const [answers, setAnswers] = useState<string[]>(() => open.map(() => ""));

  const when = (iso: string) => format.dateTime(new Date(iso), { dateStyle: "medium", timeStyle: "short" });

  async function sendAnswers(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    if (answers.some((a) => !a.trim())) {
      setFormError(t("view.answerEvery"));
      return;
    }
    setBusy(true);
    setFormError(null);
    const result = await answerQuestions(view.requestNo, answers.map((a) => a.trim()));
    setBusy(false);
    if (result.ok) await onChanged();
    else setFormError(errorMessage(result.failure));
  }

  async function openInstitution() {
    if (!view.institutionSlug) return;
    setBusy(true);
    const result = await session.switchInstitution(view.institutionSlug);
    setBusy(false);
    if (result.ok) router.push("/");
    else setFormError(t("view.openFailed"));
  }

  const reason = view.rejectReason && reasonName(view.rejectReason);

  return (
    <div className="grid gap-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <AuthHeading title={view.proposedName} description={t("view.requestNo", { requestNo: view.requestNo })} focusOnMount />
        <StatusBadge status={view.status} />
      </div>
      <FormError>{formError}</FormError>

      <div className="rounded-cup border border-hairline bg-surface p-4">
        {view.status === "Submitted" && <p>{t("view.submittedNote")}</p>}
        {view.status === "UnderReview" && <p>{t("view.underReviewNote")}</p>}
        {view.status === "Withdrawn" && <p>{t("view.withdrawnNote")}</p>}
        {view.status === "Rejected" && (
          <div className="grid gap-1">
            <p>{t("view.rejectedNote")}</p>
            {reason && <p>{t("view.reason", { reason })}</p>}
            {view.decisionNote && <p>{t("view.note", { note: view.decisionNote })}</p>}
          </div>
        )}
        {view.status === "Approved" && (
          <div className="grid gap-3">
            <p>{t("view.approvedNote", { code: view.institutionCode ?? "" })}</p>
            <Button onClick={() => void openInstitution()} loading={busy} className="justify-self-start">
              {t("view.openInstitution")}
            </Button>
          </div>
        )}
        {view.status === "InfoRequested" && <p>{t("view.questionsHelp")}</p>}
      </div>

      {view.questions.length > 0 && (
        <section className="grid gap-4">
          <h2 className="text-lg font-semibold text-brand">{t("view.questionsTitle")}</h2>
          {view.questions
            .filter((q) => q.response !== null)
            .map((q) => (
              <div key={q.number} className="rounded-cup border border-hairline p-4 text-sm">
                <p className="font-medium">{q.question}</p>
                <p className="mt-1 text-muted">{t("view.answered", { response: q.response ?? "" })}</p>
              </div>
            ))}

          {view.status === "InfoRequested" && open.length > 0 && (
            <form noValidate onSubmit={sendAnswers} className="grid gap-4">
              {open.map((q, index) => (
                <Field key={q.number} label={q.question}>
                  <textarea
                    rows={3}
                    className={cn(inputClasses, "min-h-24")}
                    aria-label={t("view.answer")}
                    value={answers[index] ?? ""}
                    onChange={(event) =>
                      setAnswers((previous) => previous.map((a, i) => (i === index ? event.target.value : a)))
                    }
                  />
                </Field>
              ))}
              <Button type="submit" size="lg" loading={busy} className="w-full">
                {t("view.sendAnswers")}
              </Button>
            </form>
          )}
        </section>
      )}

      {view.status === "InfoRequested" && (
        <>
          <Button variant="outline" onClick={onEdit}>
            {t("view.change")}
          </Button>
          <DocumentsStep view={view} lookups={lookups} focusOnMount={false} onChanged={onChanged} />
        </>
      )}

      <section className="grid gap-2">
        <h2 className="text-lg font-semibold text-brand">{t("view.history")}</h2>
        <ol className="grid gap-1 text-sm">
          {view.history.map((change, index) => (
            <li key={index} className="flex flex-wrap justify-between gap-2 border-b border-hairline py-2">
              <StatusBadge status={change.toStatus} />
              <span className="text-muted">{when(change.changedAt)}</span>
            </li>
          ))}
        </ol>
      </section>

      {canWithdraw(view.status) && <WithdrawControl requestNo={view.requestNo} onWithdrawn={onChanged} />}
    </div>
  );
}
