"use client";

import { useTranslations } from "next-intl";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { ShieldIcon } from "@/components/ui/icons";
import { useToast } from "@/components/ui/toast";
import { useRouter } from "@/i18n/navigation";
import { session } from "@/lib/auth";
import { useSession } from "@/lib/auth/useSession";
import { usePermissions } from "@/lib/permissions/usePermissions";

/** Whether the session is platform support inside an institution (plan 04, Y1). */
export function useSupportMode(): boolean {
  const state = useSession();
  const { isPlatform } = usePermissions();
  return state.status === "authenticated" && isPlatform && state.activeInstitution !== null;
}

/**
 * Plan 04, Y4: never silent. While platform support works inside an institution, a gold band under
 * the top bar names it, says every change is recorded under their name, and leads back out to the
 * platform's own screens.
 */
export function SupportBanner() {
  const t = useTranslations("Shell.support");
  const tErrors = useTranslations("Errors");
  const state = useSession();
  const support = useSupportMode();
  const router = useRouter();
  const toast = useToast();
  const [leaving, setLeaving] = useState(false);

  if (!support || state.status !== "authenticated") return null;
  const name = state.institutions.find((i) => i.slug === state.activeInstitution)?.name ?? state.activeInstitution;

  async function leave() {
    setLeaving(true);
    const result = await session.switchToPersonal();
    setLeaving(false);
    if (result.ok) router.push("/admin/institutions");
    else toast({ variant: "error", title: tErrors("network") });
  }

  return (
    <div role="status" className="border-b border-accent-strong/40 bg-accent text-on-accent">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2 px-4 py-2 lg:px-6">
        <ShieldIcon className="size-5 shrink-0" />
        <p className="min-w-0 flex-1 font-medium">{t("banner", { institution: name ?? "" })}</p>
        <Button variant="outline" className="border-ink text-ink hover:bg-ink/10" loading={leaving} onClick={() => void leave()}>
          {t("leave")}
        </Button>
      </div>
    </div>
  );
}
