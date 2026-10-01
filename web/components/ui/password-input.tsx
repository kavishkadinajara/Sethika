"use client";

import { useTranslations } from "next-intl";
import { useId, useState, type ComponentProps } from "react";
import { cn, focusRing } from "@/lib/utils";
import { EyeIcon, EyeOffIcon } from "./icons";
import { inputClasses } from "./input";

/**
 * A password field with a show/hide toggle: on a phone keyboard a typo is easy and invisible.
 * The toggle is a real 44 px button, next in the tab order after the field, with its pressed
 * state announced. Field's id and aria-* land on the <input>.
 */
export function PasswordInput({ className, ...props }: Omit<ComponentProps<"input">, "type">) {
  const t = useTranslations("Common");
  const [visible, setVisible] = useState(false);
  const fallbackId = useId();
  const inputId = props.id ?? fallbackId;

  return (
    <div className="relative">
      <input
        {...props}
        id={inputId}
        type={visible ? "text" : "password"}
        spellCheck={false}
        autoCapitalize="none"
        className={cn(inputClasses, "pr-14", className)}
      />
      <button
        type="button"
        aria-controls={inputId}
        aria-pressed={visible}
        onClick={() => setVisible((v) => !v)}
        className={cn(
          "absolute top-1/2 right-0.5 inline-flex size-11 -translate-y-1/2 items-center justify-center rounded-full text-muted",
          "hover:text-brand",
          focusRing,
        )}
      >
        {visible ? <EyeOffIcon /> : <EyeIcon />}
        <span className="sr-only">{visible ? t("hidePassword") : t("showPassword")}</span>
      </button>
    </div>
  );
}
