"use client";

import { useTranslations } from "next-intl";
import { Sheet, SheetBody, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { StepUpForm } from "./StepUpForm";

/** Loaded lazily by StepUpHost the first time an action asks for a step-up. */
export default function StepUpDialog({ onDone }: { onDone: (verified: boolean) => void }) {
  const t = useTranslations("StepUp");

  return (
    <Sheet open onOpenChange={(open) => !open && onDone(false)}>
      <SheetContent
        side="bottom"
        className="sm:inset-x-auto sm:bottom-6 sm:left-1/2 sm:w-full sm:max-w-md sm:-translate-x-1/2 sm:rounded-bowl"
      >
        <SheetHeader>
          <SheetTitle>{t("title")}</SheetTitle>
          <SheetDescription>{t("description")}</SheetDescription>
        </SheetHeader>
        <SheetBody>
          <StepUpForm onVerified={() => onDone(true)} onCancel={() => onDone(false)} />
        </SheetBody>
      </SheetContent>
    </Sheet>
  );
}
