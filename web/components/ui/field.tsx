"use client";

import { useTranslations } from "next-intl";
import { Slot } from "radix-ui";
import { useId, type ReactElement, type ReactNode } from "react";
import { cn } from "@/lib/utils";
import { AlertCircleIcon } from "./icons";
import { Label } from "./label";

type FieldProps = {
  label: ReactNode;
  /** Help text, read before the control and tied to it with aria-describedby. */
  description?: ReactNode;
  /** When set, the control is marked aria-invalid and the message is announced. */
  error?: ReactNode;
  required?: boolean;
  /** Defaults to a generated id. */
  id?: string;
  className?: string;
  /** Exactly one control: Input, OtpInput or any element that takes id and aria-* props. */
  children: ReactElement;
};

/**
 * Label, description, control and error in one block, wired for assistive technology:
 * the label targets the control, description and error are in its aria-describedby,
 * and the error sits in a live region that is always mounted, so a message appearing
 * after submit is announced.
 */
export function Field({ label, description, error, required, id, className, children }: FieldProps) {
  const t = useTranslations("Common");
  const generated = useId();
  const controlId = id ?? generated;
  const descriptionId = `${controlId}-description`;
  const errorId = `${controlId}-error`;
  const invalid = error != null && error !== false && error !== "";

  const describedBy = [description ? descriptionId : null, invalid ? errorId : null]
    .filter(Boolean)
    .join(" ");

  return (
    <div className={cn("min-w-0", className)}>
      <div className="grid gap-1.5">
        <Label htmlFor={controlId}>
          {label}
          {/* The space keeps the accessible name "Phone (required)", not "Phone(required)". */}
          {required && " "}
          {required && <span className="text-sm font-normal text-muted">({t("required")})</span>}
        </Label>

        {description && (
          <p id={descriptionId} className="text-sm leading-normal text-muted">
            {description}
          </p>
        )}

        <Slot.Root
          id={controlId}
          aria-describedby={describedBy || undefined}
          aria-invalid={invalid || undefined}
          aria-required={required || undefined}
        >
          {children}
        </Slot.Root>
      </div>

      {/* Never display:none - a live region must already be in the tree when text arrives. */}
      <p
        id={errorId}
        aria-live="polite"
        className={cn("text-sm leading-normal text-danger", invalid && "mt-1.5")}
      >
        {invalid && (
          <span className="flex items-start gap-1.5">
            <AlertCircleIcon className="mt-0.5 size-4" />
            <span>{error}</span>
          </span>
        )}
      </p>
    </div>
  );
}
