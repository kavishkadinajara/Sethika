"use client";

import { useTranslations } from "next-intl";
import { useState, type FormEvent } from "react";
import { FormError } from "@/components/auth/FormError";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { useToast } from "@/components/ui/toast";
import { checklistAnswers, checklistDraft, type ChecklistDraft } from "@/lib/onboarding/review";
import { recordChecklist } from "@/lib/onboarding/review-api";
import type { ReviewView } from "@/lib/onboarding/types";
import { cn } from "@/lib/utils";
import { useReviewError } from "./useReviewError";

const choices = [
  { value: true, key: "yes" },
  { value: false, key: "no" },
  { value: null, key: "notYet" },
] as const;

/** The checks a reviewer makes beyond the documents. Read-only unless the review is on. */
export function ChecklistForm({ view, canReview, onChanged }: { view: ReviewView; canReview: boolean; onChanged: () => Promise<void> }) {
  const t = useTranslations("Review.checklist");
  const errorMessage = useReviewError();
  const toast = useToast();
  const [draft, setDraft] = useState<ChecklistDraft>(() => checklistDraft(view.checklist));
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const set = (code: string, change: Partial<ChecklistDraft[string]>) =>
    setDraft((previous) => ({ ...previous, [code]: { ...previous[code], ...change } }));

  async function save(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    const answers = checklistAnswers(draft);
    if (answers.length === 0) return setError(t("nothingAnswered"));
    setBusy(true);
    setError(null);
    const result = await recordChecklist(view.application.requestNo, answers);
    setBusy(false);
    if (!result.ok) return setError(errorMessage(result.failure));
    toast({ title: t("saved"), variant: "success" });
    await onChanged();
  }

  return (
    <section className="grid gap-3">
      <h2 className="text-lg font-semibold text-brand">{t("title")}</h2>
      {view.checklist.length === 0 ? (
        <p className="text-sm text-muted">{t("none")}</p>
      ) : (
        <form noValidate onSubmit={save} className="grid gap-3">
          {canReview && <p className="text-sm text-muted">{t("help")}</p>}
          {view.checklist.map((item) => {
            const answer = draft[item.code];
            return (
              <fieldset key={item.code} className="grid gap-3 rounded-cup border border-hairline bg-surface p-4" disabled={!canReview}>
                <legend className="px-1 font-medium text-fg">{item.question}</legend>
                {item.isMandatory && <p className="-mt-2 text-sm text-muted">{t("required")}</p>}
                <div className="flex flex-wrap gap-2">
                  {choices.map((choice) => (
                    <label
                      key={choice.key}
                      className={cn(
                        "inline-flex min-h-11 cursor-pointer items-center gap-2 rounded-full border border-hairline px-4",
                        "has-checked:border-brand has-checked:bg-brand/10 has-checked:text-brand",
                        "has-focus-visible:outline-2 has-focus-visible:outline-offset-2 has-focus-visible:outline-brand",
                        "has-disabled:cursor-default",
                      )}
                    >
                      <input
                        type="radio"
                        name={`check-${item.code}`}
                        checked={answer.passed === choice.value}
                        onChange={() => set(item.code, { passed: choice.value })}
                        className="size-4 accent-[var(--color-primary)]"
                      />
                      {t(choice.key)}
                    </label>
                  ))}
                </div>
                <label className="grid gap-1 text-sm">
                  <span className="text-muted">{t("remarks")}</span>
                  <Input value={answer.remarks} onChange={(event) => set(item.code, { remarks: event.target.value })} />
                </label>
              </fieldset>
            );
          })}
          <FormError>{error}</FormError>
          {canReview && (
            <Button type="submit" loading={busy} className="justify-self-start">
              {t("save")}
            </Button>
          )}
        </form>
      )}
    </section>
  );
}
