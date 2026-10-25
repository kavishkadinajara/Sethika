"use client";

import { useLocale, useTranslations } from "next-intl";
import { useState, type FormEvent } from "react";
import { FormError } from "@/components/auth/FormError";
import { useWide } from "@/components/bookings/useWide";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Input, inputClasses } from "@/components/ui/input";
import { Sheet, SheetBody, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { routing } from "@/i18n/routing";
import { createUser } from "@/lib/admin/api";
import { useAdminError } from "./useAdminError";

/**
 * An account made by the platform for someone who cannot sign up themselves (plan 04, Y6): a name,
 * a phone and the language of their texts. No password: they sign in with a texted code, which is
 * also the proof that the number is theirs.
 */
export function CreateUserSheet({ onClose, onCreated }: { onClose: () => void; onCreated: (name: string) => void }) {
  const t = useTranslations("Admin.create");
  const tLanguages = useTranslations("LanguageSwitcher");
  const errorMessage = useAdminError();
  const wide = useWide();
  const locale = useLocale();

  const [name, setName] = useState("");
  const [phone, setPhone] = useState("");
  const [language, setLanguage] = useState<string>(locale);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<{ message: string; field: "name" | "phone" | null } | null>(null);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setSaving(true);
    setError(null);
    const result = await createUser({ fullName: name.trim(), phone: phone.trim(), language });
    setSaving(false);
    if (!result.ok) {
      const code = result.failure.code;
      setError({
        message: errorMessage(result.failure),
        field: code === "invalid_full_name" ? "name" : code === "invalid_phone_number" || code === "already_registered" ? "phone" : null,
      });
      return;
    }
    onCreated(name.trim());
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
              <Input value={name} onChange={(event) => setName(event.target.value)} autoComplete="off" maxLength={150} />
            </Field>
            <Field label={t("phone")} required error={error?.field === "phone" ? error.message : null}>
              <Input type="tel" inputMode="tel" value={phone} onChange={(event) => setPhone(event.target.value)} autoComplete="off" placeholder="077 123 4567" />
            </Field>
            <Field label={t("language")} required>
              <select className={inputClasses} value={language} onChange={(event) => setLanguage(event.target.value)}>
                {routing.locales.map((l) => (
                  <option key={l} value={l} lang={l}>
                    {tLanguages(`names.${l}`)}
                  </option>
                ))}
              </select>
            </Field>
          </SheetBody>
          <SheetFooter>
            <Button type="submit" loading={saving} disabled={!name.trim() || !phone.trim()}>
              {t("create")}
            </Button>
          </SheetFooter>
        </form>
      </SheetContent>
    </Sheet>
  );
}
