"use client";

import { useFormatter, useLocale, useTranslations } from "next-intl";
import { useSearchParams } from "next/navigation";
import { useCallback, useEffect, useState } from "react";
import { FormError } from "@/components/auth/FormError";
import { textLink } from "@/components/auth/links";
import { StatusBadge } from "@/components/onboarding/StatusBadge";
import { Field } from "@/components/ui/field";
import { inputClasses } from "@/components/ui/input";
import { Spinner } from "@/components/ui/spinner";
import { Link, useRouter } from "@/i18n/navigation";
import { localName } from "@/lib/onboarding/progress";
import { pageCount, parseQueueFilter, queueFilters, type QueueFilter } from "@/lib/onboarding/review";
import { getReviewLookups, listForReview } from "@/lib/onboarding/review-api";
import type { Lookups, ReviewQueuePage } from "@/lib/onboarding/types";
import { cn, focusRing } from "@/lib/utils";

/**
 * The reviewer's queue, oldest submitted first. The filter and the page are in the address
 * (?status=&page=), so a filtered queue can be shared and the back button returns to it.
 */
export function ReviewQueue() {
  const t = useTranslations("Review");
  const format = useFormatter();
  const locale = useLocale();
  const router = useRouter();
  const params = useSearchParams();
  const status = parseQueueFilter(params.get("status"));
  const page = Math.max(1, Number.parseInt(params.get("page") ?? "1", 10) || 1);

  const [lookups, setLookups] = useState<Lookups | null>(null);
  const [queue, setQueue] = useState<ReviewQueuePage | null>(null);
  const [failed, setFailed] = useState(false);

  const load = useCallback(async () => {
    setFailed(false);
    setQueue(null);
    const [names, list] = await Promise.all([getReviewLookups(locale), listForReview(status, page)]);
    if (names.ok && list.ok) {
      setLookups(names.value);
      setQueue(list.value);
    } else {
      setFailed(true);
    }
  }, [locale, status, page]);

  useEffect(() => {
    // Loading the queue is what this component is for; the state it sets is the fetched data.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load]);

  const address = (next: { status?: QueueFilter; page?: number }) => {
    const query = new URLSearchParams();
    const s = next.status === undefined ? status : next.status;
    if (s) query.set("status", s);
    const p = next.page ?? 1;
    if (p > 1) query.set("page", String(p));
    const text = query.toString();
    return `/admin/registrations${text ? `?${text}` : ""}`;
  };

  const typeName = (code: string) => {
    const type = lookups?.institutionTypes.find((x) => x.code === code);
    return type ? localName(type, locale) : code;
  };
  const districtName = (code: string) => {
    const district = lookups?.districts.find((x) => x.code === code);
    return district ? localName(district, locale) : code;
  };

  const pages = queue ? pageCount(queue.total, queue.pageSize) : 1;

  return (
    <section className="grid gap-5">
      <Field label={t("filter")}>
        <select
          className={inputClasses}
          value={status ?? ""}
          onChange={(event) => router.replace(address({ status: parseQueueFilter(event.target.value || null) }))}
        >
          {queueFilters.map((filter) => (
            <option key={filter ?? "open"} value={filter ?? ""}>
              {t(`filters.${filter ?? "open"}`)}
            </option>
          ))}
        </select>
      </Field>

      {failed && (
        <FormError>
          {t("loadFailed")}{" "}
          <button type="button" onClick={() => void load()} className={cn(textLink, focusRing, "min-h-0")}>
            {t("retry")}
          </button>
        </FormError>
      )}

      {!failed && !queue && (
        <div className="flex justify-center py-10 text-brand">
          <Spinner />
        </div>
      )}

      {queue && (
        <>
          <p className="text-sm text-muted" aria-live="polite">
            {t("count", { total: queue.total })}
          </p>

          {queue.items.length === 0 ? (
            <p className="text-muted">{t("none")}</p>
          ) : (
            <ul className="grid gap-3 lg:grid-cols-2 2xl:grid-cols-3">
              {queue.items.map((item) => (
                <li key={item.requestNo} className="rounded-cup border border-hairline bg-surface p-4">
                  <div className="flex flex-wrap items-start justify-between gap-3">
                    <div className="grid min-w-0 gap-1">
                      <p className="font-medium break-words text-fg">{item.proposedName}</p>
                      <p className="text-sm text-muted">
                        {typeName(item.institutionType)} · {item.city}, {districtName(item.district)}
                      </p>
                      <p className="text-sm text-muted">
                        {item.requestNo}
                        {item.submittedAt &&
                          ` · ${t("submittedOn", { date: format.dateTime(new Date(item.submittedAt), { dateStyle: "medium" }) })}`}
                      </p>
                    </div>
                    <StatusBadge status={item.status} viewer="reviewer" />
                  </div>
                  <Link
                    href={`/admin/registrations/${encodeURIComponent(item.requestNo)}`}
                    className={cn(textLink, focusRing, "mt-1")}
                    aria-label={`${t("open")}: ${item.proposedName}`}
                  >
                    {t("open")}
                  </Link>
                </li>
              ))}
            </ul>
          )}

          {pages > 1 && (
            <nav className="flex flex-wrap items-center justify-between gap-3" aria-label={t("pageOf", { page, pages })}>
              {page > 1 ? (
                <Link href={address({ page: page - 1 })} className={cn(textLink, focusRing)}>
                  {t("previous")}
                </Link>
              ) : (
                <span />
              )}
              <span className="text-sm text-muted">{t("pageOf", { page, pages })}</span>
              {page < pages ? (
                <Link href={address({ page: page + 1 })} className={cn(textLink, focusRing)}>
                  {t("next")}
                </Link>
              ) : (
                <span />
              )}
            </nav>
          )}
        </>
      )}
    </section>
  );
}
