"use client";

import type { ReactNode } from "react";
import { Spinner } from "@/components/ui/spinner";
import { cn, focusRing } from "@/lib/utils";

type SwitchProps = {
  checked: boolean;
  onChange: (checked: boolean) => void;
  /** The setting, in words; the switch's accessible name. */
  children: ReactNode;
  disabled?: boolean;
  busy?: boolean;
};

/**
 * On or off, for a setting that takes effect at once (an ARIA switch). The whole row is the
 * target, so it is easy to hit, and the label wraps rather than clipping.
 */
export function Switch({ checked, onChange, children, disabled = false, busy = false }: SwitchProps) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-busy={busy || undefined}
      disabled={disabled || busy}
      onClick={() => onChange(!checked)}
      className={cn(
        "flex min-h-11 w-full items-center justify-between gap-4 rounded-cup px-1 text-left text-base text-fg",
        "disabled:cursor-not-allowed disabled:opacity-60",
        focusRing,
      )}
    >
      <span className="min-w-0">{children}</span>
      <span
        aria-hidden
        className={cn(
          "relative inline-flex h-7 w-12 shrink-0 items-center rounded-full border-[1.5px] transition-colors duration-150",
          checked ? "border-primary bg-primary" : "border-line bg-subtle",
        )}
      >
        <span
          className={cn(
            "absolute grid size-5 place-items-center rounded-full bg-surface shadow-sm transition-[left] duration-150 motion-reduce:transition-none",
            checked ? "left-[1.375rem]" : "left-0.5",
          )}
        >
          {busy && <Spinner size="sm" decorative />}
        </span>
      </span>
    </button>
  );
}
