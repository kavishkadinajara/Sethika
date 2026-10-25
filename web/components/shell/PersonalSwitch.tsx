"use client";

import { useTranslations } from "next-intl";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { useToast } from "@/components/ui/toast";
import { useRouter } from "@/i18n/navigation";
import { session } from "@/lib/auth";

/**
 * Takes an institution's session back to the person's own account (plan 04, P1), where a donor's
 * screens hold (plan 02, B1). It stays on the same page: the gate that showed it then opens.
 */
export function PersonalSwitch({ className }: { className?: string }) {
  const t = useTranslations("Common");
  const tErrors = useTranslations("Errors");
  const toast = useToast();
  const router = useRouter();
  const [busy, setBusy] = useState(false);

  async function go() {
    setBusy(true);
    const result = await session.switchToPersonal();
    setBusy(false);
    if (result.ok) return;
    if (result.reason === "signed_out") {
      router.replace("/login");
      return;
    }
    toast({ variant: "error", title: tErrors("network") });
  }

  return (
    <Button variant="outline" loading={busy} onClick={() => void go()} className={className}>
      {t("switchToPersonal")}
    </Button>
  );
}
