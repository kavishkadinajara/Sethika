"use client";

import { useTranslations } from "next-intl";
import { useRef, useState, type FormEvent, type ReactNode } from "react";
import { AuthHeading } from "@/components/auth/AuthHeading";
import { FormError } from "@/components/auth/FormError";
import { textLink } from "@/components/auth/links";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { useToast } from "@/components/ui/toast";
import { downloadDocument, uploadDocument } from "@/lib/onboarding/api";
import { acceptAttribute, documentsFor, fileProblem, missingBeforeSubmit } from "@/lib/onboarding/progress";
import type { ApplicationView, DocumentMeta, Lookups, RequiredDocument, UploadedDocument } from "@/lib/onboarding/types";
import { cn, focusRing } from "@/lib/utils";
import { useOnboardingError } from "./useOnboardingError";

/**
 * Step 3: a file for each document the institution type provides. Each uploads on its own, so a
 * slow connection loses one file at most; `onChanged` reloads the application to show the result.
 */
export function DocumentsStep({
  view,
  lookups,
  focusOnMount,
  onChanged,
  onContinue,
  onBack,
}: {
  view: ApplicationView;
  lookups: Lookups;
  focusOnMount: boolean;
  onChanged: () => Promise<void>;
  onContinue?: () => void;
  onBack?: () => void;
}) {
  const t = useTranslations("Apply");
  const [formError, setFormError] = useState<ReactNode>(null);
  const missing = missingBeforeSubmit(view, lookups).documents;

  function next() {
    if (missing.length > 0) {
      setFormError(t("documents.missing"));
      return;
    }
    onContinue?.();
  }

  return (
    <div className="grid gap-5">
      <AuthHeading title={t("documents.title")} description={t("documents.help")} focusOnMount={focusOnMount} />
      <FormError>{formError}</FormError>

      {documentsFor(lookups, view.institutionType).map((document) => (
        <DocumentUpload
          key={document.code}
          requestNo={view.requestNo}
          document={document}
          uploaded={view.documents.find((d) => d.documentType === document.code) ?? null}
          onUploaded={async () => {
            setFormError(null);
            await onChanged();
          }}
        />
      ))}

      {onContinue && (
        <Button size="lg" className="w-full" onClick={next}>
          {t("saveContinue")}
        </Button>
      )}
      {onBack && (
        <Button variant="ghost" onClick={onBack}>
          {t("back")}
        </Button>
      )}
    </div>
  );
}

function DocumentUpload({
  requestNo,
  document,
  uploaded,
  onUploaded,
}: {
  requestNo: string;
  document: RequiredDocument;
  uploaded: UploadedDocument | null;
  onUploaded: () => Promise<void>;
}) {
  const t = useTranslations("Apply");
  const errorMessage = useOnboardingError();
  const toast = useToast();
  const fileInput = useRef<HTMLInputElement>(null);
  const [file, setFile] = useState<File | null>(null);
  const [meta, setMeta] = useState<DocumentMeta>({ referenceNo: "", issuedBy: "", issuedDate: "", expiryDate: "" });
  const [invalid, setInvalid] = useState<Set<string>>(new Set());
  const [error, setError] = useState<ReactNode>(null);
  const [busy, setBusy] = useState(false);

  const types = acceptAttribute(document).replaceAll(".", "").toUpperCase().replaceAll(",", ", ");

  function choose(chosen: File | null) {
    setError(null);
    setFile(chosen);
    if (!chosen) return;
    const problem = fileProblem(document, chosen);
    if (problem === "type") setError(t("documents.fileType", { types }));
    if (problem === "size") setError(t("documents.fileSize", { size: document.maxSizeMb }));
    if (problem === "empty") setError(t("documents.fileEmpty"));
  }

  async function upload(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    setInvalid(new Set());
    if (!file) {
      setError(t("documents.fileRequired"));
      return;
    }
    if (fileProblem(document, file)) return;

    setBusy(true);
    setError(null);
    const result = await uploadDocument(requestNo, document.code, file, meta);
    if (result.ok) {
      setFile(null);
      if (fileInput.current) fileInput.current.value = "";
      await onUploaded();
      toast({ title: t("saved"), variant: "success" });
    } else if (result.failure.fields.length > 0) {
      setInvalid(new Set(result.failure.fields));
      setError(t("fieldsInvalid"));
    } else {
      setError(errorMessage(result.failure));
    }
    setBusy(false);
  }

  async function download() {
    const result = await downloadDocument(requestNo, document.code);
    if (!result.ok) {
      setError(errorMessage(result.failure));
      return;
    }
    const url = URL.createObjectURL(result.value);
    const link = window.document.createElement("a");
    link.href = url;
    link.download = uploaded?.fileName ?? document.code;
    link.click();
    URL.revokeObjectURL(url);
  }

  const metaField = (key: keyof DocumentMeta, show: boolean, required: boolean, type = "text") =>
    show && (
      <Field label={t(`documents.${key}`)} error={invalid.has(key) ? t("fieldInvalid") : undefined} required={required}>
        <Input type={type} value={meta[key]} onChange={(event) => setMeta((previous) => ({ ...previous, [key]: event.target.value }))} />
      </Field>
    );

  return (
    <form noValidate onSubmit={upload} className="grid gap-4 rounded-cup border border-hairline bg-surface p-4">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <h3 className="font-semibold text-brand">{document.name}</h3>
        <span className="text-sm text-muted">{document.isMandatory ? t("documents.required") : t("documents.optional")}</span>
      </div>
      {(document.instructions || document.description) && (
        <p className="-mt-2 text-sm text-muted">{document.instructions || document.description}</p>
      )}

      {uploaded && (
        <div className="grid gap-1 rounded-md bg-subtle p-3 text-sm">
          <p>
            {t("documents.uploaded", { name: uploaded.fileName })} · {t(`documents.${uploaded.verificationStatus}`)}
          </p>
          {uploaded.reviewerRemarks && <p className="text-fg">{t("documents.remarks", { remarks: uploaded.reviewerRemarks })}</p>}
          <button type="button" onClick={() => void download()} className={cn(textLink, focusRing, "min-h-0 justify-self-start")}>
            {t("documents.download")}
          </button>
        </div>
      )}

      <Field label={t("documents.file")} description={t("documents.fileHelp", { types, size: document.maxSizeMb })} error={error}>
        <input
          ref={fileInput}
          type="file"
          accept={acceptAttribute(document)}
          onChange={(event) => choose(event.target.files?.[0] ?? null)}
          className={cn("block w-full text-base file:mr-4 file:rounded-full file:border-0 file:bg-brand/10 file:px-4 file:py-2 file:text-brand", focusRing)}
        />
      </Field>

      {metaField("referenceNo", true, document.requiresReferenceNo)}
      {metaField("issuedBy", true, false)}
      {metaField("issuedDate", true, document.requiresIssueDate, "date")}
      {metaField("expiryDate", document.requiresExpiryDate, true, "date")}

      <Button type="submit" variant={uploaded ? "outline" : "primary"} loading={busy}>
        {uploaded ? t("documents.replace") : t("documents.upload")}
      </Button>
    </form>
  );
}
