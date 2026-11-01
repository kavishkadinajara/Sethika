"use client";

import { useTranslations } from "next-intl";
import type { AppointmentStatus } from "@/lib/services/types";
import { cn } from "@/lib/utils";

const tone: Record<AppointmentStatus, string> = {
  Requested: "border-accent/60 bg-accent/15 text-fg",
  Approved: "border-brand bg-primary text-on-primary",
  Completed: "border-brand/30 bg-brand/10 text-brand",
  Rejected: "border-danger/40 bg-danger-soft text-fg",
  Cancelled: "border-hairline bg-subtle text-muted",
};

/** An appointment's status in words; the colour only repeats what the words say (as BookingStatusBadge). */
export function ServiceStatusBadge({ status, className }: { status: AppointmentStatus; className?: string }) {
  const t = useTranslations("Services.status");
  return (
    <span className={cn("inline-flex max-w-full shrink-0 items-center rounded-full border px-3 py-1 text-sm font-medium", tone[status], className)}>
      {t(status)}
    </span>
  );
}
