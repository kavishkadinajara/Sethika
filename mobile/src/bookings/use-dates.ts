import { useMemo } from "react";

import { useI18n } from "@/i18n/i18n-provider";

import { createDates, type Dates } from "./dates";

/** Dates in words in the screen's language (dates.ts). */
export function useDates(): Dates {
  const { language, t } = useI18n();
  return useMemo(() => createDates(language, t), [language, t]);
}
