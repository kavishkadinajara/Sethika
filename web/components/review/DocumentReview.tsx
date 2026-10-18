"use client";

import { useFormatter, useTranslations } from "next-intl";
import { useState, type FormEvent } from "react";
import { FormError } from "@/components/auth/FormError";
import { textLink } from "@/components/auth/links";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { inputClasses } from "@/components/ui/input";
import { useToast } from "@/components/ui/toast";
import { MAX_REMARKS_LENGTH, uploadFor } from "@/lib/onboarding/review";
import { downloadForReview, verifyDocument } from "@/lib/onboarding/review-api";
import type { ReviewRequiredDocument, ReviewView } from "@/lib/onboarding/types";
import { cn, focusRing } from "@/lib/utils";
import { useReviewError } from "./useReviewError";

/** Every document the institution type provides: the upload, and - under review - a verdict on it. */
export function DocumentReview({ view, canReview, onChanged }: { view: ReviewView; canReview: boolean; onChanged: () => Promise<void> }) {
  const t = useTranslations("Review.documents");
  return (
    <section className="grid gap-3">
      <h2 className="text-lg font-semibold text-brand">{t("title")}</h2>
      <p className="text-sm text-muted">{t("notScanned")}</p>
      <ul className="grid gap-3">
        {view.requiredDocuments.map((document) => (
          <DocumentItem key={document.code} view={view} document={document} canReview={canReview} onChanged={onChanged} />
        ))}
      </ul>
    </section>
  );
}

function DocumentItem({
  view,
  document,
  canReview,
  onChanged,
}: {
  view: ReviewView;
  document: ReviewRequiredDocument;
  canReview: boolean;
  onChanged: () => Promise<void>;
}) {
  const t = useTranslations("Review.documents");
  const apply = useTranslations("Apply.documents");
  const format = useFormatter();
  const errorMessage = useReviewError();
  const toast = useToast();
  const upload = uploadFor(view, document.code);
  const requestNo = view.application.requestNo;

  const [refusing, setRefusing] = useState(false);
  const [remarks, setRemarks] = useState("");
  const [remarksError, setRemarksError] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function decide(verified: boolean, text: string | null) {
    setBusy(true);
    setError(null);
    const result = await verifyDocument(requestNo, document.code, verified, text);
    setBusy(false);
    if (!result.ok) {
      setError(errorMessage(result.failure));
      return;
    }
    setRefusing(false);
    setRemarks("");
    toast({ title: verified ? t("accepted") : t("refused"), variant: "success" });
    await onChanged();
  }

  function refuse(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    const text = remarks.trim();
    if (!text) return setRemarksError(t("remarksRequired"));
    if (text.length > MAX_REMARKS_LENGTH) return setRemarksError(t("remarksTooLong", { max: MAX_REMARKS_LENGTH }));
    setRemarksError(null);
    void decide(false, text);
  }

  async function download() {
    const result = await downloadForReview(requestNo, document.code);
    if (!result.ok) return setError(errorMessage(result.failure));
    const url = URL.createObjectURL(result.value);
    const link = window.document.createElement("a");
    link.href = url;
    link.download = upload?.fileName ?? document.code;
    link.click();
    URL.revokeObjectURL(url);
  }

  return (
    <li className="grid gap-3 rounded-cup border border-hairline bg-surface p-4">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <h3 className="font-semibold text-brand">{document.name}</h3>
        <span className="text-sm text-muted">{document.isMandatory ? apply("required") : apply("optional")}</span>
      </div>

      {!upload ? (
        <p className="text-sm text-muted">{t("notUploaded")}</p>
      ) : (
        <div className="grid gap-1 text-sm">
          <p className="break-words">
            {t("uploaded", { name: upload.fileName, date: format.dateTime(new Date(upload.uploadedAt), { dateStyle: "medium" }) })}
          </p>
          {upload.referenceNo && <p className="text-muted">{apply("referenceNo")}: {upload.referenceNo}</p>}
          {upload.issuedBy && <p className="text-muted">{apply("issuedBy")}: {upload.issuedBy}</p>}
          {upload.issuedDate && <p className="text-muted">{apply("issuedDate")}: {upload.issuedDate}</p>}
          {upload.expiryDate && <p className="text-muted">{apply("expiryDate")}: {upload.expiryDate}</p>}
          <p className={cn("font-medium", upload.verificationStatus === "Rejected" ? "text-danger" : upload.verificationStatus === "Verified" ? "text-brand" : "text-fg")}>
            {apply(upload.verificationStatus)}
          </p>
          {upload.reviewerRemarks && <p>{apply("remarks", { remarks: upload.reviewerRemarks })}</p>}
          <button type="button" onClick={() => void download()} className={cn(textLink, focusRing, "min-h-0 justify-self-start")}>
            {t("download")}
          </button>
        </div>
      )}

      <FormError>{error}</FormError>

      {canReview && upload && !refusing && (
        <div className="flex flex-wrap gap-3">
          <Button loading={busy} disabled={upload.verificationStatus === "Verified"} onClick={() => void decide(true, null)}>
            {t("accept")}
          </Button>
          <Button variant="outline" disabled={busy} onClick={() => setRefusing(true)}>
            {t("refuse")}
          </Button>
        </div>
      )}

      {canReview && upload && refusing && (
        <form noValidate onSubmit={refuse} className="grid gap-3">
          <Field label={t("remarks")} description={t("remarksHelp")} error={remarksError} required>
            <textarea rows={3} className={cn(inputClasses, "min-h-24")} value={remarks} onChange={(event) => setRemarks(event.target.value)} />
          </Field>
          <div className="flex flex-wrap gap-3">
            <Button type="submit" variant="danger" loading={busy}>
              {t("saveRefusal")}
            </Button>
            <Button variant="ghost" onClick={() => setRefusing(false)}>
              {t("cancel")}
            </Button>
          </div>
        </form>
      )}
    </li>
  );
}
