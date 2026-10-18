"use client";

import { useTranslations } from "next-intl";
import type { ApplicationStatus } from "@/lib/onboarding/types";
import { cn } from "@/lib/utils";

const tone: Record<ApplicationStatus, string> = {
  Draft: "border-hairline bg-subtle text-fg",
  Submitted: "border-brand/30 bg-brand/10 text-brand",
  UnderReview: "border-brand/30 bg-brand/10 text-brand",
  InfoRequested: "border-accent/60 bg-accent/15 text-fg",
  Approved: "border-brand bg-primary text-on-primary",
  Rejected: "border-danger/40 bg-danger-soft text-fg",
  Withdrawn: "border-hairline bg-subtle text-muted",
};

/** An application's status in words; the colour only repeats what the words say. */
export function StatusBadge({ status }: { status: ApplicationStatus }) {
  const t = useTranslations("Apply.status");
  return (
    <span className={cn("inline-flex shrink-0 items-center rounded-full border px-3 py-1 text-sm font-medium", tone[status])}>
      {t(status)}
    </span>
  );
}
