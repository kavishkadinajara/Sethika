"use client";

import { useTranslations } from "next-intl";
import { useCallback } from "react";
import { hoursAndMinutes } from "@/lib/services/time";

/** "3 hours", "1 hour 30 min", "45 min" in the page's language. */
export function useDuration() {
  const t = useTranslations("Services.duration");
  return useCallback(
    (durationMinutes: number): string => {
      const { hours, minutes } = hoursAndMinutes(durationMinutes);
      if (hours === 0) return t("minutes", { count: minutes });
      if (minutes === 0) return t("hours", { count: hours });
      return t("hoursMinutes", { hours, minutes });
    },
    [t],
  );
}
