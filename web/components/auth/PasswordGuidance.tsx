"use client";

import { useTranslations } from "next-intl";
import { CheckIcon } from "@/components/ui/icons";
import type { PasswordRule } from "@/lib/auth/api";
import { cn } from "@/lib/utils";

/*  The password rules as the API applies them today (Identity's defaults: 6 characters, upper,
    lower, digit, symbol). The checklist is guidance while typing; the server decides, and its
    password_rejected list is what is shown after submit. Signup and the password reset share
    both, so the two never describe the rules differently.                                     */
const liveRules: { rule: PasswordRule; met: (password: string) => boolean }[] = [
  { rule: "TooShort", met: (p) => p.length >= 6 },
  { rule: "MissingUppercase", met: (p) => /\p{Lu}/u.test(p) },
  { rule: "MissingLowercase", met: (p) => /\p{Ll}/u.test(p) },
  { rule: "MissingDigit", met: (p) => /\p{Nd}/u.test(p) },
  { rule: "MissingNonAlphanumeric", met: (p) => /[^\p{L}\p{Nd}]/u.test(p) },
];

/** The rules the server said the password broke (password_rejected's failures list). */
export function PasswordFailures({ failures }: { failures: PasswordRule[] }) {
  const t = useTranslations("Password");
  return (
    <span className="mt-1 grid gap-0.5">
      {failures.map((rule) => (
        <span key={rule}>• {t(rule)}</span>
      ))}
    </span>
  );
}

/** Live guidance under the password field. Each rule says whether it is met, for screen readers too. */
export function PasswordChecklist({ password }: { password: string }) {
  const t = useTranslations("Password");
  return (
    <span className="grid gap-1">
      <span>{t("title")}</span>
      <span className="grid gap-0.5">
        {liveRules.map(({ rule, met }) => {
          const ok = met(password);
          return (
            <span key={rule} className={cn("flex items-center gap-2", ok ? "text-success" : "text-muted")}>
              {ok ? <CheckIcon className="size-4 shrink-0" /> : <span aria-hidden className="mx-1.5 size-1 shrink-0 rounded-full bg-current" />}
              <span>
                <span className="sr-only">{ok ? t("met") : t("notMet")} </span>
                {t(rule)}
              </span>
            </span>
          );
        })}
      </span>
    </span>
  );
}
