"use client";

import { useTranslations } from "next-intl";
import { useState } from "react";
import { FormError } from "@/components/auth/FormError";
import { Button } from "@/components/ui/button";
import { withdrawApplication } from "@/lib/onboarding/api";
import { useOnboardingError } from "./useOnboardingError";

/** Withdraws, after asking once: it cannot be undone. A draft counts towards the limit of three. */
export function WithdrawControl({ requestNo, onWithdrawn }: { requestNo: string; onWithdrawn: () => Promise<void> }) {
  const t = useTranslations("Apply.view");
  const errorMessage = useOnboardingError();
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function withdraw() {
    setBusy(true);
    setError(null);
    const result = await withdrawApplication(requestNo);
    setBusy(false);
    setConfirming(false);
    if (result.ok) await onWithdrawn();
    else setError(errorMessage(result.failure));
  }

  if (!confirming) {
    return (
      <div className="grid gap-2">
        <FormError>{error}</FormError>
        <Button variant="ghost" onClick={() => setConfirming(true)}>
          {t("withdraw")}
        </Button>
      </div>
    );
  }

  return (
    <div className="grid gap-3 rounded-cup border border-danger/40 bg-danger-soft p-4">
      <p>{t("withdrawConfirm")}</p>
      <div className="flex flex-wrap gap-3">
        <Button variant="danger" loading={busy} onClick={() => void withdraw()}>
          {t("withdrawYes")}
        </Button>
        <Button variant="ghost" onClick={() => setConfirming(false)}>
          {t("keep")}
        </Button>
      </div>
    </div>
  );
}
