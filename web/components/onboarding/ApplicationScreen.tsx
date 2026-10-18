"use client";

import { useLocale, useTranslations } from "next-intl";
import { useCallback, useEffect, useState } from "react";
import { FormError } from "@/components/auth/FormError";
import { textLink } from "@/components/auth/links";
import { StepProgress } from "@/components/auth/StepProgress";
import { Spinner } from "@/components/ui/spinner";
import { Link, useRouter } from "@/i18n/navigation";
import { getApplication, getLookups, type OnboardingFailure } from "@/lib/onboarding/api";
import {
  bearersFromView,
  detailsFromView,
  emptyDetails,
  isEditable,
  resumeStep,
  wizardSteps,
  type WizardStep,
} from "@/lib/onboarding/progress";
import type { ApplicationView, Lookups } from "@/lib/onboarding/types";
import { cn, focusRing } from "@/lib/utils";
import { ApplicationStatusView } from "./ApplicationStatusView";
import { DeclarationStep } from "./DeclarationStep";
import { DetailsStep } from "./DetailsStep";
import { DocumentsStep } from "./DocumentsStep";
import { OfficeBearersStep } from "./OfficeBearersStep";
import { useOnboardingError } from "./useOnboardingError";
import { WithdrawControl } from "./WithdrawControl";

/**
 * One application, from a blank form to a decision. A draft is the four-step form, resumed at the
 * first step with something left to do; anything submitted is its status, with the same steps
 * reopened (without the declaration) while the reviewer's questions are open.
 */
export function ApplicationScreen({ requestNo }: { requestNo: string | null }) {
  const t = useTranslations("Apply");
  const errorMessage = useOnboardingError();
  const locale = useLocale();
  const router = useRouter();

  const [lookups, setLookups] = useState<Lookups | null>(null);
  const [view, setView] = useState<ApplicationView | null>(null);
  const [failure, setFailure] = useState<OnboardingFailure | null>(null);
  const [step, setStep] = useState<WizardStep | null>(requestNo ? null : "details");
  const [editing, setEditing] = useState(false);
  const [moved, setMoved] = useState(false);

  const reload = useCallback(async () => {
    if (!requestNo) return;
    const result = await getApplication(requestNo);
    if (result.ok) setView(result.value);
    else setFailure(result.failure);
  }, [requestNo]);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      const loaded = await getLookups(locale);
      if (cancelled) return;
      if (!loaded.ok) {
        setFailure(loaded.failure);
        return;
      }
      setLookups(loaded.value);
      await reload();
    })();
    return () => {
      cancelled = true;
    };
  }, [locale, reload]);

  // A draft opens where its applicant left off.
  useEffect(() => {
    // The step follows from data that has just arrived; there is nothing else to derive it from.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    if (view && lookups && step === null && view.status === "Draft") setStep(resumeStep(view, lookups));
  }, [view, lookups, step]);

  function go(next: WizardStep) {
    setStep(next);
    setMoved(true);
    window.scrollTo({ top: 0 });
  }

  if (failure) {
    return (
      <div className="grid gap-4">
        <FormError>{failure.code === "registration_not_found" ? t("notFound") : errorMessage(failure)}</FormError>
        <Link href="/apply" className={cn(textLink, focusRing)}>
          {t("backToList")}
        </Link>
      </div>
    );
  }

  if (!lookups || (requestNo && !view)) {
    return (
      <div className="flex justify-center py-16 text-brand">
        <Spinner />
      </div>
    );
  }

  const back = (
    <Link href="/apply" className={cn(textLink, focusRing, "mb-4")}>
      {t("backToList")}
    </Link>
  );

  // Submitted, decided or withdrawn: the status, unless the applicant is answering questions by editing.
  if (view && view.status !== "Draft" && !(editing && isEditable(view.status))) {
    return (
      <div>
        {back}
        <ApplicationStatusView
          key={`${view.status}:${view.questions.length}`}
          view={view}
          lookups={lookups}
          onChanged={reload}
          onEdit={() => {
            setEditing(true);
            go("details");
          }}
        />
      </div>
    );
  }

  // While answering questions there is no declaration step: the application was declared already.
  const steps: readonly WizardStep[] = editing ? wizardSteps.filter((s) => s !== "declaration") : wizardSteps;
  const current = step ?? "details";
  const index = steps.indexOf(current) + 1;
  const finishEditing = async () => {
    await reload();
    setEditing(false);
  };

  return (
    <div>
      {back}
      <StepProgress current={index} total={steps.length} label={`${t("step", { current: index, total: steps.length })} · ${t(`steps.${current}`)}`} />

      {current === "details" && (
        <DetailsStep
          requestNo={requestNo}
          initial={view ? detailsFromView(view) : emptyDetails()}
          lookups={lookups}
          focusOnMount={moved}
          onSaved={async (saved) => {
            if (!requestNo) {
              // The draft exists now: its own address, where it resumes on office bearers.
              router.replace(`/apply/${encodeURIComponent(saved)}`);
              return;
            }
            await reload();
            go("bearers");
          }}
        />
      )}

      {current === "bearers" && view && (
        <OfficeBearersStep
          requestNo={view.requestNo}
          initial={bearersFromView(view)}
          focusOnMount={moved}
          onBack={() => go("details")}
          onSaved={async () => {
            await reload();
            go("documents");
          }}
        />
      )}

      {current === "documents" && view && (
        <DocumentsStep
          view={view}
          lookups={lookups}
          focusOnMount={moved}
          onChanged={reload}
          onBack={() => go("bearers")}
          onContinue={editing ? () => void finishEditing() : () => go("declaration")}
        />
      )}

      {current === "declaration" && view && (
        <DeclarationStep view={view} lookups={lookups} focusOnMount={moved} onBack={() => go("documents")} onSubmitted={reload} />
      )}

      {view?.status === "Draft" && (
        <div className="mt-8 border-t border-hairline pt-4">
          <WithdrawControl requestNo={view.requestNo} onWithdrawn={reload} />
        </div>
      )}
    </div>
  );
}
