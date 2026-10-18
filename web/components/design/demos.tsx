"use client";

import { useTranslations } from "next-intl";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import { OtpInput } from "@/components/ui/otp-input";
import {
  Sheet,
  SheetBody,
  SheetClose,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from "@/components/ui/sheet";
import { useToast } from "@/components/ui/toast";

/*  The interactive half of /design: anything that needs state or an event handler. */

export function SheetDemo() {
  const t = useTranslations("Design.sample");

  return (
    <div className="flex flex-wrap gap-3">
      {(["bottom", "right"] as const).map((side) => (
        <Sheet key={side}>
          <SheetTrigger asChild>
            <Button variant="outline">{side === "bottom" ? t("openBottom") : t("openRight")}</Button>
          </SheetTrigger>
          <SheetContent side={side}>
            <SheetHeader>
              <SheetTitle>{t("sheetTitle")}</SheetTitle>
              <SheetDescription>{t("sheetDescription")}</SheetDescription>
            </SheetHeader>
            <SheetBody>
              <Field label={t("nameLabel")}>
                <Input autoComplete="name" />
              </Field>
            </SheetBody>
            <SheetFooter>
              <SheetClose asChild>
                <Button variant="ghost">{t("cancel")}</Button>
              </SheetClose>
              <SheetClose asChild>
                <Button>{t("done")}</Button>
              </SheetClose>
            </SheetFooter>
          </SheetContent>
        </Sheet>
      ))}
    </div>
  );
}

export function ToastDemo() {
  const t = useTranslations("Design.sample");
  const toast = useToast();

  return (
    <div className="flex flex-wrap gap-3">
      <Button
        variant="outline"
        onClick={() => toast({ title: t("noticeTitle"), description: t("noticeBody") })}
      >
        {t("showDefault")}
      </Button>
      <Button
        variant="outline"
        onClick={() =>
          toast({ title: t("successTitle"), description: t("successBody"), variant: "success" })
        }
      >
        {t("showSuccess")}
      </Button>
      <Button
        variant="outline"
        onClick={() => toast({ title: t("errorTitle"), description: t("errorBody"), variant: "error" })}
      >
        {t("showError")}
      </Button>
    </div>
  );
}

/** Live field: type or paste, then Check. Anything but six digits shows the announced error. */
export function OtpFieldDemo() {
  const t = useTranslations("Design.sample");
  const [code, setCode] = useState("");
  const [error, setError] = useState<string | null>(null);

  return (
    <form
      noValidate
      className="grid gap-3"
      onSubmit={(event) => {
        event.preventDefault();
        setError(code.length === 6 ? null : t("otpError"));
      }}
    >
      <Field label={t("otpLabel")} description={t("otpDescription")} error={error} required>
        <OtpInput
          value={code}
          onValueChange={(next) => {
            setCode(next);
            setError(null);
          }}
        />
      </Field>
      <p className="text-sm text-muted" aria-hidden>
        {t("otpValue", { value: code || "-" })}
      </p>
      <div>
        <Button type="submit">{t("check")}</Button>
      </div>
    </form>
  );
}

/** Live field: Check with anything but a 10-digit 07 number shows the error. */
export function PhoneFieldDemo() {
  const t = useTranslations("Design.sample");
  const [phone, setPhone] = useState("");
  const [error, setError] = useState<string | null>(null);

  return (
    <form
      noValidate
      className="grid gap-3"
      onSubmit={(event) => {
        event.preventDefault();
        setError(/^07\d{8}$/.test(phone.replace(/\s/g, "")) ? null : t("phoneError"));
      }}
    >
      <Field label={t("phoneLabel")} description={t("phoneDescription")} error={error} required>
        <Input
          type="tel"
          inputMode="tel"
          autoComplete="tel-national"
          placeholder={t("phonePlaceholder")}
          value={phone}
          onChange={(event) => setPhone(event.target.value)}
        />
      </Field>
      <div>
        <Button type="submit">{t("check")}</Button>
      </div>
    </form>
  );
}
