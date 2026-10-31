"use client";

import { useLocale, useTranslations } from "next-intl";
import { useEffect, useState } from "react";
import { FormError } from "@/components/auth/FormError";
import { Button } from "@/components/ui/button";
import { InstitutionIcon, ShieldIcon } from "@/components/ui/icons";
import { Spinner } from "@/components/ui/spinner";
import { useToast } from "@/components/ui/toast";
import { useRouter } from "@/i18n/navigation";
import { getInstitutionLookups, listInstitutions, requestSupport } from "@/lib/admin/api";
import { institutionStatuses, parseInstitutionQuery, type InstitutionQuery } from "@/lib/admin/list-query";
import type { AdminInstitution, AdminPage } from "@/lib/admin/types";
import { localName } from "@/lib/bookings/names";
import type { Lookups } from "@/lib/onboarding/types";
import { session } from "@/lib/auth";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { cn } from "@/lib/utils";
import { FilterChips, ListCount, ListSearch, ListSelect, Pager, useListQuery } from "./ListControls";
import { useAdminError } from "./useAdminError";

const statusTone: Record<AdminInstitution["status"], string> = {
  Active: "bg-success-soft text-success",
  Onboarding: "bg-accent/20 text-fg",
  Suspended: "bg-danger-soft text-danger",
  Deactivated: "bg-subtle text-muted",
};

/**
 * Every institution on Sethika (plan 04, Y5), a page at a time, narrowed by status, type and
 * district and ordered by name or newest, all kept in the address (plan 05, X4); and the way into one as platform support (Y1). Opening
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
  const locale = useLocale();

  const [query, setQuery] = useListQuery<InstitutionQuery>(parseInstitutionQuery, "name");
  const [page, setPage] = useState<AdminPage<AdminInstitution> | null>(null);
  const [lookups, setLookups] = useState<Lookups | null>(null);
  const [failure, setFailure] = useState<string | null>(null);
  const [opening, setOpening] = useState<string | null>(null);

  useEffect(() => {
    // A later query's answer must not be overwritten by an earlier one arriving late.
    let cancelled = false;
    void listInstitutions(query).then((result) => {
      if (cancelled) return;
      if (result.ok) {
        setPage(result.value);
        setFailure(null);
      } else {
        setFailure(errorMessage(result.failure));
      }
    });
    return () => {
      cancelled = true;
    };
  }, [query, errorMessage]);

  // The type and district names, in the page's language, for the two filters that take codes.
  useEffect(() => {
    let cancelled = false;
    void getInstitutionLookups(locale).then((result) => !cancelled && result.ok && setLookups(result.value));
    return () => {
      cancelled = true;
    };
  }, [locale]);

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
      <ListSearch label={t("search")} hint={t("searchHint")} value={query.search} onSearch={(search) => setQuery({ search })} />

      <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_minmax(0,14rem)_minmax(0,14rem)_minmax(0,14rem)] lg:items-end">
        <FilterChips
          label={t("statusLabel")}
          options={institutionStatuses.map((status) => ({ value: status, label: t(`status.${status}`) }))}
          value={query.status}
          onChange={(status) => setQuery({ status })}
        />
        <ListSelect
          label={t("type")}
          options={[{ value: "", label: t("allTypes") }, ...(lookups?.institutionTypes ?? []).map((type) => ({ value: type.code, label: localName(locale, type) }))]}
          value={query.type ?? ""}
          onChange={(type) => setQuery({ type: type || null })}
        />
        <ListSelect
          label={t("district")}
          options={[{ value: "", label: t("allDistricts") }, ...(lookups?.districts ?? []).map((district) => ({ value: district.code, label: localName(locale, district) }))]}
          value={query.district ?? ""}
          onChange={(district) => setQuery({ district: district || null })}
        />
        <ListSelect
          label={t("sort")}
          options={[
            { value: "name", label: t("sortName") },
            { value: "newest", label: t("sortNewest") },
          ]}
          value={query.sort}
          onChange={(sort) => setQuery({ sort: sort === "newest" ? "newest" : "name" })}
        />
      </div>

      <FormError>{failure}</FormError>

      {page !== null && <ListCount page={page.page} shown={page.items.length} total={page.total} />}

      {page === null ? (
        <div className="flex justify-center py-16 text-brand">
          <Spinner />
        </div>
      ) : page.items.length === 0 ? (
        <p className="text-muted">{t("empty")}</p>
      ) : (
        <ul aria-label={t("list")} className="grid grid-cols-1 gap-3 lg:grid-cols-2 2xl:grid-cols-3">
          {page.items.map((institution) => (
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

      {page !== null && <Pager page={page.page} total={page.total} onPage={(next) => setQuery({ page: next })} />}
    </div>
  );
}
