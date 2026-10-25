"use client";

import { useFormatter, useTranslations } from "next-intl";
import { useCallback, useEffect, useState } from "react";
import { FormError } from "@/components/auth/FormError";
import { textLink } from "@/components/auth/links";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { Link } from "@/i18n/navigation";
import { listApplications } from "@/lib/onboarding/api";
import type { ApplicationSummary } from "@/lib/onboarding/types";
import { cn, focusRing } from "@/lib/utils";
import { StatusBadge } from "./StatusBadge";

/** The applicant's own applications, newest first, and the way to start another. */
export function ApplicationList() {
  const t = useTranslations("Apply");
  const format = useFormatter();
  const [applications, setApplications] = useState<ApplicationSummary[] | null>(null);
  const [failed, setFailed] = useState(false);

  const load = useCallback(async () => {
    setFailed(false);
    const result = await listApplications();
    if (result.ok) setApplications(result.value);
    else setFailed(true);
  }, []);

  useEffect(() => {
    // Loading the list is what this component is for; the state it sets is the fetched data.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load]);

  if (failed) {
    return (
      <FormError>
        {t("loadFailed")}{" "}
        <button type="button" onClick={() => void load()} className={cn(textLink, focusRing, "min-h-0")}>
          {t("retry")}
        </button>
      </FormError>
    );
  }

  if (applications === null) {
    return (
      <div className="flex justify-center py-10 text-brand">
        <Spinner />
      </div>
    );
  }

  return (
    <section className="grid gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-xl font-semibold text-brand">{t("mine")}</h2>
        <Button asChild>
          <Link href="/apply/new">{t("start")}</Link>
        </Button>
      </div>

      {applications.length === 0 ? (
        <p className="text-muted">{t("none")}</p>
      ) : (
        <ul className="grid gap-3 lg:grid-cols-2 2xl:grid-cols-3">
          {applications.map((application) => (
            <li key={application.requestNo} className="rounded-cup border border-hairline bg-surface p-4">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="grid min-w-0 gap-1">
                  <p className="font-medium text-fg">{application.proposedName}</p>
                  <p className="text-sm text-muted">
                    {application.requestNo} ·{" "}
                    {application.submittedAt
                      ? t("submittedOn", { date: format.dateTime(new Date(application.submittedAt), { dateStyle: "medium" }) })
                      : t("startedOn", { date: format.dateTime(new Date(application.createdAt), { dateStyle: "medium" }) })}
                  </p>
                </div>
                <StatusBadge status={application.status} />
              </div>
              <Link
                href={`/apply/${encodeURIComponent(application.requestNo)}`}
                className={cn(textLink, focusRing, "mt-1")}
              >
                {t("open")}
              </Link>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
