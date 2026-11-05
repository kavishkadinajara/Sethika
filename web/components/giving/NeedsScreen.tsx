"use client";

import { useTranslations } from "next-intl";
import { useCallback, useEffect, useState } from "react";
import { FormError } from "@/components/auth/FormError";
import { textLink } from "@/components/auth/links";
import { EmptyState } from "@/components/bookings/EmptyState";
import { useDates } from "@/components/bookings/useDates";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { useToast } from "@/components/ui/toast";
import type { BookingFailure } from "@/lib/bookings/api";
import { listNeeds, stepNeed } from "@/lib/giving/api";
import { needSteps } from "@/lib/giving/needs";
import type { NeedListItem } from "@/lib/giving/types";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { cn, focusRing } from "@/lib/utils";
import { NeedPledgesSheet } from "./NeedPledgesSheet";
import { NeedSheet } from "./NeedSheet";
import { NeedProgress, PriorityTag, useNeedLookups } from "./NeedText";
import { useGivingError } from "./useGivingError";

/**
 * The institution's needs (plan 09, §5): open ones first, each with its progress. A need starts as a
 * draft nobody sees and is opened onto the public board (M11). Pledges are received or cancelled in its
 * sheet; a need waits for them before it closes.
 */
export function NeedsScreen() {
  const t = useTranslations("Giving.needs");
  const dates = useDates();
  const toast = useToast();
  const errorMessage = useGivingError();
  const { lookups, category, unit } = useNeedLookups();
  const { can } = usePermissions();
  const canAdd = can("GIVING.NEEDS.CREATE");
  const canEdit = can("GIVING.NEEDS.EDIT");

  const [rows, setRows] = useState<NeedListItem[] | null>(null);
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [adding, setAdding] = useState(false);
  const [editing, setEditing] = useState<NeedListItem | null>(null);
  const [pledgesOf, setPledgesOf] = useState<NeedListItem | null>(null);

  const load = useCallback(async () => {
    setFailure(null);
    const result = await listNeeds();
    if (!result.ok) return setFailure(result.failure);
    setRows(result.value);
  }, []);

  useEffect(() => {
    // Loading the needs is what this component is for.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load]);

  async function step(need: NeedListItem, which: "open" | "close" | "cancel") {
    setFailure(null);
    const result = await stepNeed(need.publicId, which, need.version);
    if (!result.ok) return setFailure(result.failure);
    toast({ variant: "success", title: t(`stepped.${which}`, { need: need.title }) });
    void load();
  }

  if (failure && !rows) {
    return (
      <FormError>
        {errorMessage(failure)}{" "}
        <button type="button" onClick={() => void load()} className={cn(textLink, focusRing, "min-h-0")}>
          {t("retry")}
        </button>
      </FormError>
    );
  }

  if (!rows) {
    return (
      <div className="flex justify-center py-16 text-brand">
        <Spinner />
      </div>
    );
  }

  return (
    <div className="grid gap-6">
      <FormError>{failure && errorMessage(failure)}</FormError>

      {canAdd && (
        <div>
          <Button onClick={() => setAdding(true)}>{t("add")}</Button>
        </div>
      )}

      {rows.length === 0 ? (
        <EmptyState>{t("none")}</EmptyState>
      ) : (
        <ul className="grid gap-3 lg:grid-cols-2 2xl:grid-cols-3" aria-label={t("list")}>
          {rows.map((need) => {
            const steps = needSteps(need);
            return (
              <li key={need.publicId}>
                <article aria-label={need.title} className="grid h-full content-start gap-2 rounded-bowl border border-hairline bg-surface p-4 shadow-lift">
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <h2 className="min-w-0 break-words text-lg font-semibold leading-snug text-brand">{need.title}</h2>
                    <span className="rounded-full border border-hairline px-3 py-1 text-sm font-semibold text-fg">{t(`status.${need.status}`)}</span>
                  </div>
                  <p className="text-sm text-muted">
                    {category(need.category)}
                    {need.deadline && <> · {t("deadline", { date: dates.medium(need.deadline) })}</>}
                  </p>
                  {need.priority === "Urgent" && (
                    <div>
                      <PriorityTag priority={need.priority} />
                    </div>
                  )}
                  <NeedProgress need={need} unit={unit(need.unit)} />
                  <div className="flex flex-wrap gap-2 pt-1">
                    {need.status !== "Draft" && (
                      <Button variant="outline" onClick={() => setPledgesOf(need)} aria-label={t("pledgesNamed", { need: need.title })}>
                        {t("pledges", { count: need.openPledges })}
                      </Button>
                    )}
                    {canEdit && steps.open && (
                      <Button onClick={() => void step(need, "open")} aria-label={t("openNamed", { need: need.title })}>
                        {t("open")}
                      </Button>
                    )}
                    {canEdit && steps.edit && (
                      <Button variant="ghost" onClick={() => setEditing(need)} aria-label={t("editNamed", { need: need.title })}>
                        {t("edit")}
                      </Button>
                    )}
                    {canEdit && steps.close && (
                      <Button variant="ghost" onClick={() => void step(need, "close")} aria-label={t("closeNamed", { need: need.title })}>
                        {t("close")}
                      </Button>
                    )}
                    {canEdit && steps.cancel && (
                      <Button variant="ghost" onClick={() => void step(need, "cancel")} aria-label={t("cancelNamed", { need: need.title })}>
                        {t("cancel")}
                      </Button>
                    )}
                  </div>
                </article>
              </li>
            );
          })}
        </ul>
      )}

      {(adding || editing) && (
        <NeedSheet
          need={editing}
          lookups={lookups}
          onClose={() => {
            setAdding(false);
            setEditing(null);
          }}
          onSaved={() => {
            toast({ variant: "success", title: editing ? t("saved") : t("added") });
            setAdding(false);
            setEditing(null);
            void load();
          }}
        />
      )}

      {pledgesOf && <NeedPledgesSheet need={pledgesOf} unit={unit(pledgesOf.unit)} onClose={() => setPledgesOf(null)} onChanged={() => void load()} />}
    </div>
  );
}
