"use client";

import { useTranslations } from "next-intl";
import { Spinner } from "@/components/ui/spinner";
import { Link, useRouter } from "@/i18n/navigation";
import { readNextPath } from "@/lib/auth/next-path";
import { useSession } from "@/lib/auth/useSession";
import { cn, focusRing } from "@/lib/utils";
import { AuthHeading } from "./AuthHeading";
import { textLink } from "./links";
import { StepUpForm } from "./StepUpForm";

/** /stepup: the step-up as a page, for a link that asks for it before a sensitive screen. */
export function StepUpPage() {
  const t = useTranslations("StepUp");
  const router = useRouter();
  const state = useSession();

  if (state.status === "loading") {
    return (
      <div className="flex justify-center py-10 text-brand">
        <Spinner />
      </div>
    );
  }

  return (
    <>
      <AuthHeading title={t("title")} description={state.status === "authenticated" ? t("description") : t("signInFirst")} />
      {state.status === "authenticated" ? (
        <StepUpForm onVerified={() => router.replace(readNextPath())} />
      ) : (
        <Link href={`/login?next=${encodeURIComponent("/stepup")}`} className={cn(textLink, focusRing)}>
          {t("signIn")}
        </Link>
      )}
    </>
  );
}
