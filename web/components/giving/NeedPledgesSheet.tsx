"use client";

import { useTranslations } from "next-intl";
import { useCallback, useEffect, useState, type FormEvent } from "react";
import { FormError } from "@/components/auth/FormError";
import { textLink } from "@/components/auth/links";
import { useDates } from "@/components/bookings/useDates";
import { useWide } from "@/components/bookings/useWide";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Input, inputClasses } from "@/components/ui/input";
import { Sheet, SheetBody, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { useToast } from "@/components/ui/toast";
import { Link } from "@/i18n/navigation";
import type { BookingFailure } from "@/lib/bookings/api";
import { cancelPledge, listNeedPledges, receivePledge } from "@/lib/giving/api";
import { formatQuantity } from "@/lib/giving/money";
import type { NeedListItem, NeedPledge } from "@/lib/giving/types";
import { usePermissions } from "@/lib/permissions/usePermissions";
import { localPhone } from "@/lib/sponsors/donors";
import { cn, focusRing } from "@/lib/utils";
import { useGivingError } from "./useGivingError";

type Acting = { pledge: NeedPledge; action: "receive" | "cancel" };

/**
 * A need's pledges (plan 09, M13, M15): who promised what, and what became of it. Staff mark what actually
 * came, which writes an in-kind receipt, or cancel a promise with a reason the donor will read.
 */
export function NeedPledgesSheet({ need, unit, onClose, onChanged }: { need: NeedListItem; unit: string; onClose: () => void; onChanged: () => void }) {
  const t = useTranslations("Giving.pledges");
  const wide = useWide();
  const dates = useDates();
  const toast = useToast();
  const errorMessage = useGivingError();
  const { can } = usePermissions();
  const canAct = can("GIVING.NEEDS.EDIT");
  const [rows, setRows] = useState<NeedPledge[] | null>(null);
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [acting, setActing] = useState<Acting | null>(null);

  const load = useCallback(async () => {
    setFailure(null);
    const result = await listNeedPledges(need.publicId);
    if (!result.ok) return setFailure(result.failure);
    setRows(result.value);
  }, [need.publicId]);

  useEffect(() => {
    // Loading the pledges is what this sheet is for.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load();
  }, [load]);

  function done(title: string) {
    setActing(null);
    toast({ variant: "success", title });
    void load();
    onChanged();
  }

  return (
    <Sheet open onOpenChange={(open) => !open && onClose()}>
      <SheetContent side={wide ? "right" : "bottom"} className={wide ? "max-w-xl" : undefined}>
        <SheetHeader>
          <SheetTitle>{t("title", { need: need.title })}</SheetTitle>
          <SheetDescription>{t("description")}</SheetDescription>
        </SheetHeader>
        <SheetBody className="grid gap-4">
          <FormError>{failure && errorMessage(failure)}</FormError>
          {!rows && !failure && (
            <div className="flex justify-center py-12 text-brand">
              <Spinner />
            </div>
          )}
          {rows?.length === 0 && <p className="text-muted">{t("none")}</p>}
          {rows && rows.length > 0 && (
            <ul className="grid gap-3" aria-label={t("list")}>
              {rows.map((row) => (
                <li key={row.publicId}>
                  <article aria-label={row.donorName} className="grid gap-1.5 rounded-bowl border border-hairline p-3">
                    <div className="flex flex-wrap items-start justify-between gap-2">
                      <h3 className="min-w-0 break-words font-semibold text-brand">{row.donorName}</h3>
                      <span className="text-sm font-medium text-muted">{t(`status.${row.status}`)}</span>
                    </div>
                    <p className="text-fg">
                      {t("promised", { count: formatQuantity(row.quantity), unit })}
                      {row.expectedDate && <span className="text-muted"> · {t("expected", { date: dates.medium(row.expectedDate) })}</span>}
                    </p>
                    {row.donorPhone && <p className="text-sm tabular-nums text-muted">{localPhone(row.donorPhone)}</p>}
                    {row.status === "Delivered" && row.receivedQuantity !== null && <p className="text-sm text-fg">{t("came", { count: formatQuantity(row.receivedQuantity), unit })}</p>}
                    {row.cancelReason && <p className="break-words text-sm text-muted">{t("reasonShown", { reason: row.cancelReason })}</p>}
                    {row.receiptPublicId && row.receiptNo && (
                      <p className="text-sm">
                        <Link href={`/receipts/${row.receiptPublicId}`} className={cn(textLink, focusRing, "min-h-0")}>
                          {t("receiptNamed", { receiptNo: row.receiptNo })}
                        </Link>
                      </p>
                    )}
                    {row.status === "Pledged" && canAct && (
                      <div className="flex flex-wrap gap-2 pt-1">
                        <Button variant="outline" onClick={() => setActing({ pledge: row, action: "receive" })} aria-label={t("receiveNamed", { name: row.donorName })}>
                          {t("receive")}
                        </Button>
                        <Button variant="ghost" onClick={() => setActing({ pledge: row, action: "cancel" })} aria-label={t("cancelNamed", { name: row.donorName })}>
                          {t("cancelPledge")}
                        </Button>
                      </div>
                    )}
                  </article>
                </li>
              ))}
            </ul>
          )}
        </SheetBody>
        <SheetFooter>
          <Button variant="ghost" onClick={onClose}>
            {t("close")}
          </Button>
        </SheetFooter>

        {acting?.action === "receive" && (
          <ReceiveSheet
            pledge={acting.pledge}
            unit={unit}
            onClose={() => setActing(null)}
            onDone={(receiptNo) => done(t("received", { receiptNo }))}
          />
        )}
        {acting?.action === "cancel" && <CancelSheet pledge={acting.pledge} onClose={() => setActing(null)} onDone={() => done(t("cancelled"))} />}
      </SheetContent>
    </Sheet>
  );
}

function ReceiveSheet({ pledge, unit, onClose, onDone }: { pledge: NeedPledge; unit: string; onClose: () => void; onDone: (receiptNo: string) => void }) {
  const t = useTranslations("Giving.pledges");
  const wide = useWide();
  const errorMessage = useGivingError();
  const [quantity, setQuantity] = useState(String(pledge.quantity));
  const [invalid, setInvalid] = useState(false);
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [saving, setSaving] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setFailure(null);
    const value = Number(quantity.trim().replaceAll(",", ""));
    if (quantity.trim() === "" || !(value > 0) || value > pledge.quantity) return setInvalid(true);
    setInvalid(false);

    setSaving(true);
    const result = await receivePledge(pledge.publicId, value, pledge.version);
    setSaving(false);

    if (result.ok) return onDone(result.value.receiptNo);
    if (result.failure.code === "invalid_details") return setInvalid(true);
    setFailure(result.failure);
  }

  return (
    <Sheet open onOpenChange={(open) => !open && onClose()}>
      <SheetContent side={wide ? "right" : "bottom"} className={wide ? "max-w-lg" : undefined}>
        <form onSubmit={submit} noValidate className="contents">
          <SheetHeader>
            <SheetTitle>{t("receiveTitle", { name: pledge.donorName })}</SheetTitle>
            <SheetDescription>{t("receiveDescription", { count: formatQuantity(pledge.quantity), unit })}</SheetDescription>
          </SheetHeader>
          <SheetBody className="grid gap-5">
            <Field label={t("cameLabel", { unit })} description={t("cameHint")} error={invalid ? t("cameInvalid", { count: formatQuantity(pledge.quantity) }) : null} required>
              <Input inputMode="decimal" value={quantity} onChange={(e) => setQuantity(e.target.value)} autoComplete="off" className="max-w-48" />
            </Field>
            <FormError>{failure && errorMessage(failure)}</FormError>
          </SheetBody>
          <SheetFooter>
            <Button variant="ghost" onClick={onClose}>
              {t("back")}
            </Button>
            <Button type="submit" loading={saving}>
              {t("receiveConfirm")}
            </Button>
          </SheetFooter>
        </form>
      </SheetContent>
    </Sheet>
  );
}

function CancelSheet({ pledge, onClose, onDone }: { pledge: NeedPledge; onClose: () => void; onDone: () => void }) {
  const t = useTranslations("Giving.pledges");
  const wide = useWide();
  const errorMessage = useGivingError();
  const [reason, setReason] = useState("");
  const [invalid, setInvalid] = useState(false);
  const [failure, setFailure] = useState<BookingFailure | null>(null);
  const [saving, setSaving] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setFailure(null);
    if (reason.trim() === "") return setInvalid(true);
    setInvalid(false);

    setSaving(true);
    const result = await cancelPledge(pledge.publicId, reason.trim(), pledge.version);
    setSaving(false);

    if (result.ok) return onDone();
    if (result.failure.code === "invalid_details") return setInvalid(true);
    setFailure(result.failure);
  }

  return (
    <Sheet open onOpenChange={(open) => !open && onClose()}>
      <SheetContent side={wide ? "right" : "bottom"} className={wide ? "max-w-lg" : undefined}>
        <form onSubmit={submit} noValidate className="contents">
          <SheetHeader>
            <SheetTitle>{t("cancelTitle", { name: pledge.donorName })}</SheetTitle>
            <SheetDescription>{t("cancelDescription")}</SheetDescription>
          </SheetHeader>
          <SheetBody className="grid gap-5">
            <Field label={t("reason")} error={invalid ? t("reasonMissing") : null} required>
              <textarea value={reason} onChange={(e) => setReason(e.target.value)} maxLength={300} rows={3} className={cn(inputClasses, "resize-y")} />
            </Field>
            <FormError>{failure && errorMessage(failure)}</FormError>
          </SheetBody>
          <SheetFooter>
            <Button variant="ghost" onClick={onClose}>
              {t("back")}
            </Button>
            <Button type="submit" variant="danger" loading={saving}>
              {t("cancelConfirm")}
            </Button>
          </SheetFooter>
        </form>
      </SheetContent>
    </Sheet>
  );
}
