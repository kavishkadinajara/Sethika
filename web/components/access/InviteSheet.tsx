"use client";

import { useTranslations } from "next-intl";
import { useState, type FormEvent } from "react";
import { FormError } from "@/components/auth/FormError";
import { useWide } from "@/components/bookings/useWide";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Input, inputClasses } from "@/components/ui/input";
import { Sheet, SheetBody, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { invite } from "@/lib/access/api";
import type { InstitutionRole } from "@/lib/access/types";
import { useAccessError } from "./useAccessError";
import { useAccessLabels } from "./useAccessLabels";

/**
 * Invites an existing Sethika account, by phone, to one of the roles the caller may hand out
 * (plan 04, X1, X3). A number with no account is said plainly, with what to do about it.
 */
export function InviteSheet({
  roles,
  onClose,
  onInvited,
}: {
  /** Only roles the caller may manage. */
  roles: InstitutionRole[];
  onClose: () => void;
  onInvited: (phone: string) => void;
}) {
  const t = useTranslations("Access.invite");
  const labels = useAccessLabels();
  const errorMessage = useAccessError();
  const wide = useWide();

  const [phone, setPhone] = useState("");
  const [role, setRole] = useState(roles[roles.length - 1]?.code ?? "");
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<{ message: string; onPhone: boolean } | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setSending(true);
    setError(null);
    const result = await invite(phone.trim(), role);
    setSending(false);
    if (!result.ok) {
      const code = result.failure.code;
      setError({
        message: errorMessage(result.failure),
        onPhone: code === "invalid_phone_number" || code === "account_not_found" || code === "already_member" || code === "invitation_pending" || code === "own_roles",
      });
      return;
    }
    onInvited(phone.trim());
  }

  return (
    <Sheet open onOpenChange={(open) => !open && onClose()}>
      <SheetContent side={wide ? "right" : "bottom"} className={wide ? "max-w-lg" : undefined}>
        <form onSubmit={(event) => void submit(event)} className="flex min-h-full flex-col" noValidate>
          <SheetHeader>
            <SheetTitle>{t("title")}</SheetTitle>
            <SheetDescription>{t("description")}</SheetDescription>
          </SheetHeader>
          <SheetBody className="grid gap-5">
            {error && !error.onPhone && <FormError>{error.message}</FormError>}
            <Field label={t("phone")} required error={error?.onPhone ? error.message : null}>
              <Input type="tel" inputMode="tel" autoComplete="off" value={phone} onChange={(event) => setPhone(event.target.value)} placeholder="077 123 4567" />
            </Field>
            <Field label={t("role")} required>
              <select className={inputClasses} value={role} onChange={(event) => setRole(event.target.value)}>
                {roles.map((r) => (
                  <option key={r.code} value={r.code}>
                    {labels.role(r)} · {labels.level(r.level, r.levelName)}
                  </option>
                ))}
              </select>
            </Field>
          </SheetBody>
          <SheetFooter>
            <Button type="submit" loading={sending} disabled={!phone.trim() || !role}>
              {t("send")}
            </Button>
          </SheetFooter>
        </form>
      </SheetContent>
    </Sheet>
  );
}
