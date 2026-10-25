"use client";

import { useTranslations } from "next-intl";
import { useState, type FormEvent } from "react";
import { FormError } from "@/components/auth/FormError";
import { useWide } from "@/components/bookings/useWide";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Input, inputClasses } from "@/components/ui/input";
import { Sheet, SheetBody, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { createRole } from "@/lib/access/api";
import { isRoleCode, roleCodeFrom } from "@/lib/access/matrix";
import type { GrantableLevel, InstitutionRole } from "@/lib/access/types";
import { useAccessError } from "./useAccessError";
import { useAccessLabels } from "./useAccessLabels";

/**
 * A new role at a level the caller may manage (plan 01, 5.9). Its code follows its name until
 * typed over; "start from" copies another role's permissions, keeping only those the caller may
 * give at the chosen level, so the request is never refused for what the form offered.
 */
export function CreateRoleSheet({
  levels,
  roles,
  onClose,
  onCreated,
}: {
  levels: GrantableLevel[];
  roles: InstitutionRole[];
  onClose: () => void;
  onCreated: (code: string, name: string) => void;
}) {
  const t = useTranslations("Access.create");
  const tErrors = useTranslations("Access.errors");
  const labels = useAccessLabels();
  const errorMessage = useAccessError();
  const wide = useWide();

  const [name, setName] = useState("");
  const [code, setCode] = useState("");
  const [codeTouched, setCodeTouched] = useState(false);
  const [level, setLevel] = useState(levels[levels.length - 1]?.level ?? "");
  const [from, setFrom] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<{ message: string; field: "name" | "code" | "level" | null } | null>(null);

  const shownCode = codeTouched ? code : roleCodeFrom(name);

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!name.trim()) return setError({ message: tErrors("invalid_role_name"), field: "name" });
    if (!isRoleCode(shownCode)) return setError({ message: tErrors("invalid_role_code"), field: "code" });

    const grantable = new Set(levels.find((l) => l.level === level)?.permissions ?? []);
    const permissions = (roles.find((r) => r.code === from)?.permissions ?? []).filter((p) => grantable.has(p));

    setSaving(true);
    setError(null);
    const result = await createRole({ code: shownCode, name: name.trim(), level, permissions });
    setSaving(false);
    if (!result.ok) {
      const field = result.failure.code === "invalid_role_name" ? "name" : result.failure.code === "role_exists" || result.failure.code === "invalid_role_code" ? "code" : null;
      setError({ message: errorMessage(result.failure), field });
      return;
    }
    onCreated(shownCode, name.trim());
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
            {error && error.field === null && <FormError>{error.message}</FormError>}
            <Field label={t("name")} required error={error?.field === "name" ? error.message : null}>
              <Input value={name} onChange={(event) => setName(event.target.value)} maxLength={100} autoComplete="off" />
            </Field>
            <Field label={t("code")} description={t("codeHint")} required error={error?.field === "code" ? error.message : null}>
              <Input
                value={shownCode}
                onChange={(event) => {
                  setCodeTouched(true);
                  setCode(event.target.value.toUpperCase());
                }}
                maxLength={50}
                autoComplete="off"
                spellCheck={false}
                className="font-system tracking-wide"
              />
            </Field>
            <Field label={t("level")} description={t("levelHint")} required>
              <select className={inputClasses} value={level} onChange={(event) => setLevel(event.target.value)}>
                {levels.map((l) => (
                  <option key={l.level} value={l.level}>
                    {labels.level(l.level, l.name)}
                  </option>
                ))}
              </select>
            </Field>
            <Field label={t("startFrom")}>
              <select className={inputClasses} value={from} onChange={(event) => setFrom(event.target.value)}>
                <option value="">{t("startEmpty")}</option>
                {roles.map((r) => (
                  <option key={r.code} value={r.code}>
                    {labels.role(r)}
                  </option>
                ))}
              </select>
            </Field>
          </SheetBody>
          <SheetFooter>
            <Button type="submit" loading={saving}>
              {t("create")}
            </Button>
          </SheetFooter>
        </form>
      </SheetContent>
    </Sheet>
  );
}
