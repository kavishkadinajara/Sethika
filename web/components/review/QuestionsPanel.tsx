"use client";

import { useFormatter, useTranslations } from "next-intl";
import { useState, type FormEvent } from "react";
import { FormError } from "@/components/auth/FormError";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { inputClasses } from "@/components/ui/input";
import { useToast } from "@/components/ui/toast";
import { MAX_QUESTION_LENGTH, MAX_QUESTIONS, questionsToAsk } from "@/lib/onboarding/review";
import { askQuestions } from "@/lib/onboarding/review-api";
import type { ReviewView } from "@/lib/onboarding/types";
import { cn } from "@/lib/utils";
import { useReviewError } from "./useReviewError";

/** Earlier questions with their answers, and - under review - new ones, which send the application back. */
export function QuestionsPanel({ view, canReview, onChanged }: { view: ReviewView; canReview: boolean; onChanged: () => Promise<void> }) {
  const t = useTranslations("Review.questions");
  const format = useFormatter();
  const errorMessage = useReviewError();
  const toast = useToast();
  const [drafts, setDrafts] = useState<string[]>([""]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const asked = view.application.questions;

  async function send(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    const questions = questionsToAsk(drafts);
    if (questions.length === 0) return setError(t("required"));
    if (questions.some((q) => q.length > MAX_QUESTION_LENGTH)) return setError(t("tooLong", { max: MAX_QUESTION_LENGTH }));
    setBusy(true);
    setError(null);
    const result = await askQuestions(view.application.requestNo, questions);
    setBusy(false);
    if (!result.ok) return setError(errorMessage(result.failure));
    setDrafts([""]);
    toast({ title: t("sent"), variant: "success" });
    await onChanged();
  }

  if (asked.length === 0 && !canReview) return null;

  return (
    <section className="grid gap-3">
      <h2 className="text-lg font-semibold text-brand">{t("title")}</h2>

      {asked.length > 0 && (
        <ul className="grid gap-3">
          {asked.map((q) => (
            <li key={q.number} className="grid gap-1 rounded-cup border border-hairline bg-surface p-4 text-sm">
              <p className="font-medium text-fg">{q.question}</p>
              <p className="text-muted">{format.dateTime(new Date(q.askedAt), { dateStyle: "medium", timeStyle: "short" })}</p>
              <p className={q.response === null ? "text-muted italic" : "text-fg"}>
                {q.response === null ? t("unanswered") : t("answer", { response: q.response })}
              </p>
            </li>
          ))}
        </ul>
      )}

      {canReview && (
        <form noValidate onSubmit={send} className="grid gap-3">
          <p className="text-sm text-muted">{t("help")}</p>
          {drafts.map((draft, index) => (
            <div key={index} className="grid gap-2">
              <Field label={t("question", { number: asked.length + index + 1 })}>
                <textarea
                  rows={3}
                  className={cn(inputClasses, "min-h-24")}
                  value={draft}
                  onChange={(event) => setDrafts((previous) => previous.map((d, i) => (i === index ? event.target.value : d)))}
                />
              </Field>
              {drafts.length > 1 && (
                <Button variant="ghost" className="justify-self-start" onClick={() => setDrafts((previous) => previous.filter((_, i) => i !== index))}>
                  {t("remove")}
                </Button>
              )}
            </div>
          ))}
          {drafts.length < MAX_QUESTIONS && (
            <Button variant="outline" className="justify-self-start" onClick={() => setDrafts((previous) => [...previous, ""])}>
              {t("add")}
            </Button>
          )}
          <FormError>{error}</FormError>
          <Button type="submit" loading={busy} className="justify-self-start">
            {t("send")}
          </Button>
        </form>
      )}
    </section>
  );
}
