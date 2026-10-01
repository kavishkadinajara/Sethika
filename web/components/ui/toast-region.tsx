"use client";

import { useTranslations } from "next-intl";
import { Toast as ToastPrimitive } from "radix-ui";
import type { ReactNode } from "react";
import { cn, focusRing } from "@/lib/utils";
import { AlertCircleIcon, CheckCircleIcon, CloseIcon, InfoCircleIcon } from "./icons";
import type { ToastEntry, ToastVariant } from "./toast";

/**
 * Loaded lazily by <Toaster>. Radix Toast handles the live-region announcement, swipe to
 * dismiss, pausing on hover and focus, and the F8 shortcut to reach the region.
 * Errors are `foreground` (announced assertively); everything else is `background`.
 */
export default function ToastRegion({
  toasts,
  onDismiss,
}: {
  toasts: ToastEntry[];
  onDismiss: (id: number) => void;
}) {
  const t = useTranslations("Toast");

  return (
    <ToastPrimitive.Provider label={t("item")} swipeDirection="right" duration={5000}>
      {toasts.map((item) => (
        <ToastItem key={item.id} entry={item} onDismiss={() => onDismiss(item.id)} />
      ))}

      <ToastPrimitive.Viewport
        // Radix fills in {hotkey} itself; next-intl must leave the placeholder alone.
        label={t("region", { hotkey: "{hotkey}" })}
        className={cn(
          "fixed inset-x-0 bottom-0 z-[100] mx-auto flex w-full max-w-md flex-col gap-2 p-4 outline-none",
          "pb-[max(1rem,env(safe-area-inset-bottom))] sm:right-0 sm:left-auto sm:mx-0",
        )}
      />
    </ToastPrimitive.Provider>
  );
}

const tones: Record<ToastVariant, { rim: string; icon: ReactNode }> = {
  default: { rim: "after:bg-estate", icon: <InfoCircleIcon className="text-estate" /> },
  success: { rim: "after:bg-success", icon: <CheckCircleIcon className="text-success" /> },
  error: { rim: "after:bg-danger", icon: <AlertCircleIcon className="text-danger" /> },
};

function ToastItem({ entry, onDismiss }: { entry: ToastEntry; onDismiss: () => void }) {
  const t = useTranslations("Common");
  const variant = entry.variant ?? "default";
  const tone = tones[variant];

  return (
    <ToastPrimitive.Root
      open={entry.open}
      onOpenChange={(open) => {
        if (!open) onDismiss();
      }}
      type={variant === "error" ? "foreground" : "background"}
      duration={entry.duration ?? (variant === "error" ? 8000 : undefined)}
      className={cn(
        "relative flex w-full items-start gap-3 rounded-bowl border border-hairline bg-surface",
        "py-3.5 pr-14 pl-4 text-ink shadow-lift",
        // The rim: a short coloured line along the top, like the logo bowl's.
        "after:absolute after:-top-px after:left-5 after:h-[3px] after:w-10 after:rounded-b-full",
        tone.rim,
        "data-[state=open]:animate-toast-in data-[state=closed]:animate-toast-out",
        "data-[swipe=move]:translate-x-(--radix-toast-swipe-move-x)",
        "data-[swipe=cancel]:translate-x-0 data-[swipe=cancel]:transition-transform",
        "data-[swipe=end]:animate-toast-swipe-out",
        "motion-reduce:animate-none",
      )}
    >
      <span className="mt-0.5 shrink-0">{tone.icon}</span>
      <div className="grid min-w-0 gap-0.5">
        <ToastPrimitive.Title className="font-medium leading-snug">{entry.title}</ToastPrimitive.Title>
        {entry.description && (
          <ToastPrimitive.Description className="text-sm leading-normal text-muted">
            {entry.description}
          </ToastPrimitive.Description>
        )}
      </div>
      <ToastPrimitive.Close
        className={cn(
          "absolute top-1.5 right-1.5 inline-flex size-11 items-center justify-center rounded-full text-muted",
          "hover:bg-estate/8 hover:text-ink",
          focusRing,
        )}
      >
        <CloseIcon className="size-4.5" />
        <span className="sr-only">{t("close")}</span>
      </ToastPrimitive.Close>
    </ToastPrimitive.Root>
  );
}
