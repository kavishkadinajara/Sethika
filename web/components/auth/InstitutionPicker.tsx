"use client";

import { useTranslations } from "next-intl";
import { useState } from "react";
import { CheckIcon } from "@/components/ui/icons";
import { Spinner } from "@/components/ui/spinner";
import { Link, useRouter } from "@/i18n/navigation";
import { session } from "@/lib/auth";
import { readNextPath } from "@/lib/auth/next-path";
import { useSession } from "@/lib/auth/useSession";
import { cn, focusRing } from "@/lib/utils";
import { AuthHeading } from "./AuthHeading";
import { FormError } from "./FormError";
import { textLink } from "./links";

/**
 * Chooses the institution the session acts for. The list comes from the session response;
 * choosing one rotates the session onto it (POST /api/auth/session/switch-institution, by slug).
 */
export function InstitutionPicker() {
  const t = useTranslations("SelectInstitution");
  const tErrors = useTranslations("Errors");
  const router = useRouter();
  const state = useSession();
  const [switching, setSwitching] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  if (state.status === "loading") {
    return (
      <div className="flex justify-center py-10 text-brand">
        <Spinner />
      </div>
    );
  }

  if (state.status !== "authenticated") {
    return (
      <>
        <AuthHeading title={t("title")} description={t("signInFirst")} />
        <Link href={`/login?next=${encodeURIComponent("/select-institution")}`} className={cn(textLink, focusRing)}>
          {t("signIn")}
        </Link>
      </>
    );
  }

  async function choose(slug: string) {
    if (switching) return;
    setError(null);
    if (state.status === "authenticated" && state.activeInstitution === slug) {
      router.replace(readNextPath());
      return;
    }
    setSwitching(slug);
    const result = await session.switchInstitution(slug);
    if (result.ok) {
      router.replace(readNextPath());
      return;
    }
    setSwitching(null);
    if (result.reason === "signed_out") {
      router.replace(`/login?next=${encodeURIComponent("/select-institution")}`);
      return;
    }
    setError(result.reason === "not_available" ? tErrors("institution_not_available") : tErrors("network"));
  }

  return (
    <>
      <AuthHeading title={t("title")} description={state.institutions.length ? t("description") : t("none")} />
      <FormError className="mb-4">{error}</FormError>

      {state.institutions.length > 0 ? (
        <ul className="grid gap-3">
          {state.institutions.map((institution) => {
            const current = institution.slug === state.activeInstitution;
            return (
              <li key={institution.slug}>
                <button
                  type="button"
                  onClick={() => void choose(institution.slug)}
                  aria-current={current ? "true" : undefined}
                  aria-busy={switching === institution.slug || undefined}
                  disabled={switching !== null}
                  className={cn(
                    "flex min-h-14 w-full items-center justify-between gap-3 rounded-cup border-[1.5px] bg-surface px-4 py-3 text-left shadow-well",
                    "text-base font-medium text-fg transition-[border-color] hover:border-brand disabled:cursor-progress",
                    current ? "border-brand" : "border-line",
                    focusRing,
                  )}
                >
                  <span className="min-w-0">{institution.name}</span>
                  {switching === institution.slug ? (
                    <Spinner size="sm" decorative />
                  ) : current ? (
                    <span className="inline-flex shrink-0 items-center gap-1 text-sm text-brand">
                      <CheckIcon className="size-4" />
                      {t("current")}
                    </span>
                  ) : null}
                </button>
              </li>
            );
          })}
        </ul>
      ) : (
        <Link href="/" className={cn(textLink, focusRing)}>
          {t("goHome")}
        </Link>
      )}
    </>
  );
}
