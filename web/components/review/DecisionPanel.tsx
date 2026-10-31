"use client";

import { useTranslations } from "next-intl";
import { useState, type FormEvent } from "react";
import { FormError } from "@/components/auth/FormError";
import { useRejectReasonName } from "@/components/onboarding/useRejectReasonName";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { inputClasses } from "@/components/ui/input";
import { useToast } from "@/components/ui/toast";
import { approvalBlockers, isReadyToApprove, MAX_NOTE_LENGTH } from "@/lib/onboarding/review";
import { approveApplication, rejectApplication } from "@/lib/onboarding/review-api";
import type { ReviewView } from "@/lib/onboarding/types";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { cn } from "@/lib/utils";
import { useReviewError } from "./useReviewError";

/**
 * Approve or do not approve, for a platform administrator. Each asks once more before it is sent,
 * and the API then asks for a texted code (step-up): apiFetch opens that dialog and retries, so
 * the call here resolves with the final answer.
 */
export function DecisionPanel({ view, onChanged }: { view: ReviewView; onChanged: () => Promise<void> }) {
  const t = useTranslations("Review.decision");
  const reasonName = useRejectReasonName();
  const apply = useTranslations("Apply");
  const errorMessage = useReviewError();
  const toast = useToast();
  const { can } = usePermissions();
  const [mode, setMode] = useState<"idle" | "approving" | "rejecting" | "confirmReject">("idle");
  const [reason, setReason] = useState("");
  const [note, setNote] = useState("");
  const [fieldErrors, setFieldErrors] = useState<{ reason?: string; note?: string }>({});
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const mayApprove = can("ONBOARDING.REQUESTS.APPROVE");
  const mayReject = can("ONBOARDING.REQUESTS.REJECT");
  const blockers = approvalBlockers(view);
  const name = view.application.proposedName;

  async function approve() {
    setBusy(true);
    setError(null);
    const result = await approveApplication(view.application.requestNo);
    setBusy(false);
    setMode("idle");
    if (!result.ok) return setError(errorMessage(result.failure));
    toast({ title: t("approved", { code: result.value.institutionCode }), variant: "success" });
    await onChanged();
  }

  function checkRejection(event: FormEvent) {
    event.preventDefault();
    const errors: typeof fieldErrors = {};
    if (!reason) errors.reason = t("reasonRequired");
    if (note.trim().length > MAX_NOTE_LENGTH) errors.note = t("noteTooLong", { max: MAX_NOTE_LENGTH });
    setFieldErrors(errors);
    if (Object.keys(errors).length === 0) setMode("confirmReject");
  }

  async function reject() {
    setBusy(true);
    setError(null);
    const result = await rejectApplication(view.application.requestNo, reason, note.trim() || null);
    setBusy(false);
    if (!result.ok) {
      setMode("rejecting");
      return setError(errorMessage(result.failure));
    }
    setMode("idle");
    toast({ title: t("rejected"), variant: "success" });
    await onChanged();
  }

  if (!mayApprove && !mayReject) {
    return (
      <section className="grid gap-2">
        <h2 className="text-lg font-semibold text-brand">{t("title")}</h2>
        <p className="text-sm text-muted">{t("onlyAdmin")}</p>
      </section>
    );
  }

  return (
    <section className="grid gap-4 rounded-cup border border-hairline bg-surface p-4">
      <h2 className="text-lg font-semibold text-brand">{t("title")}</h2>
      <p className="text-sm text-muted">{t("help")}</p>

      {mayApprove && !isReadyToApprove(view) && (blockers.documents.length > 0 || blockers.checklist.length > 0) && (
        <div className="grid gap-1 text-sm">
          <p className="font-medium text-fg">{t("blockersTitle")}</p>
          <ul className="list-disc pl-5 text-muted">
            {blockers.documents.map((d) => (
              <li key={d.code}>{t("blockerDocument", { name: d.name })}</li>
            ))}
            {blockers.checklist.map((i) => (
              <li key={i.code}>{t("blockerChecklist", { question: i.question })}</li>
            ))}
          </ul>
        </div>
      )}

      <FormError>{error}</FormError>

      {mode === "idle" && (
        <div className="flex flex-wrap gap-3">
          {mayApprove && (
            <Button disabled={!isReadyToApprove(view)} onClick={() => setMode("approving")}>
              {t("approve")}
            </Button>
          )}
          {mayReject && (
            <Button variant="outline" onClick={() => setMode("rejecting")}>
              {t("reject")}
            </Button>
          )}
        </div>
      )}

      {mode === "approving" && (
        <div className="grid gap-3 rounded-cup border border-brand/40 bg-brand/5 p-4">
          <p>{t("approveConfirm", { name })}</p>
          <div className="flex flex-wrap gap-3">
            <Button loading={busy} onClick={() => void approve()}>
              {t("approveYes")}
            </Button>
            <Button variant="ghost" disabled={busy} onClick={() => setMode("idle")}>
              {t("cancel")}
            </Button>
          </div>
        </div>
      )}

      {mode === "rejecting" && (
        <form noValidate onSubmit={checkRejection} className="grid gap-4">
          <Field label={t("reason")} error={fieldErrors.reason} required>
            <select className={inputClasses} value={reason} onChange={(event) => setReason(event.target.value)}>
              <option value="">{apply("choose")}</option>
              {view.rejectReasons.map((r) => (
                <option key={r.code} value={r.code}>
                  {reasonName(r.code, r.name)}
                </option>
              ))}
            </select>
          </Field>
          <Field label={t("note")} description={t("noteHelp")} error={fieldErrors.note}>
            <textarea rows={3} className={cn(inputClasses, "min-h-24")} value={note} onChange={(event) => setNote(event.target.value)} />
          </Field>
          <div className="flex flex-wrap gap-3">
            <Button type="submit" variant="danger">
              {t("reject")}
            </Button>
            <Button variant="ghost" onClick={() => setMode("idle")}>
              {t("cancel")}
            </Button>
          </div>
        </form>
      )}

      {mode === "confirmReject" && (
        <div className="grid gap-3 rounded-cup border border-danger/40 bg-danger-soft p-4">
          <p>{t("rejectConfirm", { name })}</p>
          <div className="flex flex-wrap gap-3">
            <Button variant="danger" loading={busy} onClick={() => void reject()}>
              {t("rejectYes")}
            </Button>
            <Button variant="ghost" disabled={busy} onClick={() => setMode("rejecting")}>
              {t("cancel")}
            </Button>
          </div>
        </div>
      )}
    </section>
  );
}
