"use client";

import { useTranslations } from "next-intl";
import { Dialog as SheetPrimitive } from "radix-ui";
import type { ComponentProps } from "react";
import { cn } from "@/lib/utils";
import { buttonVariants } from "./button";
import { CloseIcon } from "./icons";

export const Sheet = SheetPrimitive.Root;
export const SheetTrigger = SheetPrimitive.Trigger;
export const SheetClose = SheetPrimitive.Close;

const sides = {
  // Phones: a bottom sheet the thumb can reach, capped so the page stays visible above it.
  bottom: cn(
    "inset-x-0 bottom-0 max-h-[88dvh] rounded-t-3xl pb-[max(1.25rem,env(safe-area-inset-bottom))]",
    "data-[state=open]:animate-sheet-in-bottom data-[state=closed]:animate-sheet-out-bottom",
  ),
  right: cn(
    "inset-y-0 right-0 h-dvh w-full max-w-md rounded-l-3xl",
    "data-[state=open]:animate-sheet-in-right data-[state=closed]:animate-sheet-out-right",
  ),
  left: cn(
    "inset-y-0 left-0 h-dvh w-full max-w-md rounded-r-3xl",
    "data-[state=open]:animate-sheet-in-left data-[state=closed]:animate-sheet-out-left",
  ),
} as const;

type SheetContentProps = ComponentProps<typeof SheetPrimitive.Content> & {
  side?: keyof typeof sides;
};

/** Radix Dialog underneath: focus is trapped, Escape closes, focus returns to the trigger. */
export function SheetContent({ side = "bottom", className, children, ...props }: SheetContentProps) {
  const t = useTranslations("Common");

  return (
    <SheetPrimitive.Portal>
      <SheetPrimitive.Overlay
        className={cn(
          "fixed inset-0 z-50 bg-scrim backdrop-blur-[2px]",
          "data-[state=open]:animate-overlay-in data-[state=closed]:animate-overlay-out",
          "motion-reduce:animate-none",
        )}
      />
      <SheetPrimitive.Content
        className={cn(
          "fixed z-50 flex flex-col overflow-y-auto overscroll-contain bg-surface text-fg shadow-sheet",
          "focus-visible:outline-none motion-reduce:animate-none",
          sides[side],
          className,
        )}
        {...props}
      >
        {side === "bottom" && (
          <div aria-hidden className="mx-auto mt-3 h-1.5 w-12 shrink-0 rounded-full bg-hairline" />
        )}
        {children}
        <SheetPrimitive.Close
          className={cn(
            buttonVariants({ variant: "ghost", size: "icon" }),
            "absolute right-3 top-3 text-muted hover:text-fg",
          )}
        >
          <CloseIcon />
          <span className="sr-only">{t("close")}</span>
        </SheetPrimitive.Close>
      </SheetPrimitive.Content>
    </SheetPrimitive.Portal>
  );
}

export function SheetHeader({ className, ...props }: ComponentProps<"div">) {
  // Right padding keeps a long title clear of the close button.
  return <div className={cn("grid gap-1.5 px-5 pt-5 pr-16 sm:px-6", className)} {...props} />;
}

export function SheetBody({ className, ...props }: ComponentProps<"div">) {
  return <div className={cn("px-5 py-4 sm:px-6", className)} {...props} />;
}

export function SheetFooter({ className, ...props }: ComponentProps<"div">) {
  return (
    <div
      className={cn("mt-auto flex flex-wrap justify-end gap-3 px-5 pt-2 pb-5 sm:px-6", className)}
      {...props}
    />
  );
}

export function SheetTitle({ className, ...props }: ComponentProps<typeof SheetPrimitive.Title>) {
  return (
    <SheetPrimitive.Title
      className={cn("text-xl font-semibold leading-snug text-brand", className)}
      {...props}
    />
  );
}

export function SheetDescription({
  className,
  ...props
}: ComponentProps<typeof SheetPrimitive.Description>) {
  return (
    <SheetPrimitive.Description
      className={cn("text-sm leading-normal text-muted", className)}
      {...props}
    />
  );
}
