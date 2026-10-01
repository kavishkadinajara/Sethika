"use client";

import { useState, type ComponentProps } from "react";
import { cn } from "@/lib/utils";

type OtpInputProps = Omit<
  ComponentProps<"input">,
  "value" | "defaultValue" | "onChange" | "type" | "maxLength" | "size"
> & {
  length?: number;
  value?: string;
  defaultValue?: string;
  onValueChange?: (value: string) => void;
  /** Called once all digits are present, e.g. to submit without a button press. */
  onComplete?: (value: string) => void;
};

/**
 * One real <input> under six drawn boxes. A single field means:
 * - screen readers meet one labelled control, not six unlabelled ones;
 * - paste works from anywhere ("Your code is 482 913" keeps just the digits);
 * - SMS autofill (autocomplete="one-time-code") fills the whole code at once;
 * - the numeric keypad opens on phones (inputMode="numeric").
 * Field's id and aria-* props land on the input, so the label and errors stay tied to it.
 */
export function OtpInput({
  length = 6,
  value: controlled,
  defaultValue = "",
  onValueChange,
  onComplete,
  className,
  disabled,
  onFocus,
  onBlur,
  ...inputProps
}: OtpInputProps) {
  const [uncontrolled, setUncontrolled] = useState(() => digitsOnly(defaultValue, length));
  const [focused, setFocused] = useState(false);
  const value = controlled !== undefined ? digitsOnly(controlled, length) : uncontrolled;
  const invalid = inputProps["aria-invalid"] === true || inputProps["aria-invalid"] === "true";
  const activeIndex = Math.min(value.length, length - 1);

  function update(next: string) {
    const digits = digitsOnly(next, length);
    if (controlled === undefined) setUncontrolled(digits);
    onValueChange?.(digits);
    if (digits.length === length && digits !== value) onComplete?.(digits);
  }

  return (
    <div className={cn("relative w-full max-w-sm", className)}>
      <div
        aria-hidden
        // A wrong code shakes the row once; the class is re-applied, and so re-played, on each new error.
        className={cn("grid gap-1.5 sm:gap-2", invalid && "animate-shake motion-reduce:animate-none")}
        style={{ gridTemplateColumns: `repeat(${length}, minmax(0, 1fr))` }}
      >
        {Array.from({ length }, (_, index) => {
          const char = value[index];
          const active = focused && index === activeIndex;
          return (
            <div
              key={index}
              className={cn(
                // Each cell is a small cup (docs/DESIGN.md); the active one wears the focus glow.
                "relative flex h-14 items-center justify-center rounded-cup border-[1.5px] bg-surface shadow-well",
                "text-2xl font-semibold tabular-nums text-fg transition-[border-color,box-shadow] duration-150",
                char ? "border-brand/70" : "border-line",
                active && "border-brand outline-2 outline-offset-2 outline-brand ring-6 ring-accent/40",
                invalid && "border-danger bg-danger-soft/40",
                invalid && active && "outline-danger",
                disabled && "border-hairline bg-subtle text-muted shadow-none",
              )}
            >
              {char}
              {active && !char && (
                <span className="h-6 w-0.5 animate-caret rounded-full bg-brand motion-reduce:animate-none" />
              )}
            </div>
          );
        })}
      </div>

      <input
        {...inputProps}
        type="text"
        inputMode="numeric"
        autoComplete="one-time-code"
        pattern={`\\d{${length}}`}
        spellCheck={false}
        autoCorrect="off"
        disabled={disabled}
        value={value}
        onChange={(event) => update(event.target.value)}
        onFocus={(event) => {
          setFocused(true);
          moveCaretToEnd(event.currentTarget);
          onFocus?.(event);
        }}
        onBlur={(event) => {
          setFocused(false);
          onBlur?.(event);
        }}
        // Editing happens at the end only: clicking a middle box must not split the code.
        onSelect={(event) => moveCaretToEnd(event.currentTarget)}
        className={cn(
          "absolute inset-0 h-full w-full cursor-text appearance-none border-0 bg-transparent",
          "text-base text-transparent caret-transparent outline-none selection:bg-transparent",
          "disabled:cursor-not-allowed",
        )}
      />
    </div>
  );
}

function digitsOnly(text: string, length: number) {
  return text.replace(/\D/g, "").slice(0, length);
}

function moveCaretToEnd(input: HTMLInputElement) {
  const end = input.value.length;
  if (input.selectionStart !== end || input.selectionEnd !== end) input.setSelectionRange(end, end);
}
