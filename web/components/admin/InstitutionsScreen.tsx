"use client";

import { useTranslations } from "next-intl";
import { useEffect, useState } from "react";
import { FormError } from "@/components/auth/FormError";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { InstitutionIcon, ShieldIcon } from "@/components/ui/icons";
import { Input } from "@/components/ui/input";
import { Spinner } from "@/components/ui/spinner";
import { useToast } from "@/components/ui/toast";
import { useRouter } from "@/i18n/navigation";
import { listInstitutions, requestSupport } from "@/lib/admin/api";
import type { AdminInstitution } from "@/lib/admin/types";
import { session } from "@/lib/auth";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { cn } from "@/lib/utils";
import { useAdminError } from "./useAdminError";

const statusTone: Record<AdminInstitution["status"], string> = {
  Active: "bg-success-soft text-success",
  Onboarding: "bg-accent/20 text-fg",
  Suspended: "bg-danger-soft text-danger",
  Deactivated: "bg-subtle text-muted",
};

/**
 * Every institution on Sethika (plan 04, Y5), and the way into one as platform support (Y1). Opening
 * one asks for a support ticket behind a step-up, then spends it switching the session; the
 * dashboard that follows is the institution's, with every permission, under a banner (Y4).
 */
export function InstitutionsScreen() {
  const t = useTranslations("Admin.institutions");
  const tErrors = useTranslations("Errors");
  const errorMessage = useAdminError();
  const toast = useToast();
  const router = useRouter();
  const { can } = usePermissions();

  const [search, setSearch] = useState("");
  const [rows, setRows] = useState<AdminInstitution[] | null>(null);
  const [failure, setFailure] = useState<string | null>(null);
  const [opening, setOpening] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    // Typing settles for a moment before the list is read again.
    const timer = setTimeout(() => {
      void listInstitutions(search.trim()).then((result) => {
        if (cancelled) return;
        if (result.ok) {
          setRows(result.value);
          setFailure(null);
        } else {
          setFailure(errorMessage(result.failure));
        }
      });
    }, 250);
    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [search, errorMessage]);

  async function open(institution: AdminInstitution) {
    setOpening(institution.slug);
    const ticket = await requestSupport(institution.slug);
    if (!ticket.ok) {
      setOpening(null);
      toast({ variant: "error", title: errorMessage(ticket.failure) });
      return;
    }
    const switched = await session.switchInstitution(institution.slug);
    setOpening(null);
    if (switched.ok) {
      router.push("/");
      return;
    }
    toast({ variant: "error", title: switched.reason === "not_available" ? tErrors("institution_not_available") : tErrors("network") });
  }

  return (
    <div className="grid gap-6">
      <div className="max-w-xl">
        <Field label={t("search")} description={t("searchHint")}>
          <Input type="search" value={search} onChange={(event) => setSearch(event.target.value)} autoComplete="off" />
        </Field>
      </div>

      <FormError>{failure}</FormError>

      {rows === null ? (
        <div className="flex justify-center py-16 text-brand">
          <Spinner />
        </div>
      ) : rows.length === 0 ? (
        <p className="text-muted">{t("empty")}</p>
      ) : (
        <ul className="grid gap-3 lg:grid-cols-2 2xl:grid-cols-3">
          {rows.map((institution) => (
            <li key={institution.slug} className="grid content-start gap-3 rounded-bowl border border-hairline bg-surface p-5 shadow-lift">
              <div className="flex items-start gap-3">
                <span className="grid size-11 shrink-0 place-items-center rounded-2xl bg-primary/10 text-brand">
                  <InstitutionIcon className="size-5.5" />
                </span>
                <div className="grid min-w-0 flex-1 gap-0.5">
                  <p className="flex flex-wrap items-center gap-2">
                    <span className="font-semibold text-fg">{institution.name}</span>
                    <span className={cn("rounded-full px-2.5 py-0.5 text-xs font-semibold", statusTone[institution.status])}>
                      {t(`status.${institution.status}`)}
                    </span>
                  </p>
                  <p className="text-sm text-muted">
                    {[institution.type, institution.district].filter(Boolean).join(" · ")}
                  </p>
                  <p className="text-sm text-muted">
                    {institution.owner ? t("owner", { name: institution.owner }) : t("noOwner")} · {t("staff", { count: institution.staff })}
                  </p>
                </div>
              </div>
              {can("PLATFORM.INSTITUTIONS.EXECUTE") && (institution.status === "Active" || institution.status === "Onboarding") && (
                <Button
                  variant="outline"
                  className="justify-self-start"
                  loading={opening === institution.slug}
                  disabled={opening !== null}
                  onClick={() => void open(institution)}
                >
                  <ShieldIcon className="size-5" />
                  {t("open")}
                </Button>
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
