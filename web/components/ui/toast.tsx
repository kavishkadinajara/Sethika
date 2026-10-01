"use client";

import { useTranslations } from "next-intl";
import { Toast as ToastPrimitive } from "radix-ui";
import { createContext, useCallback, useContext, useRef, useState, type ReactNode } from "react";
import { cn } from "@/lib/utils";
import { AlertCircleIcon, CheckCircleIcon, CloseIcon, InfoCircleIcon } from "./icons";

export type ToastVariant = "default" | "success" | "error";

export type ToastOptions = {
  /** Already translated: pass t("..."), never a literal. */
  title: string;
  description?: string;
  variant?: ToastVariant;
  /** Milliseconds. Errors stay longer by default so they can be read. */
  duration?: number;
};

type ToastEntry = ToastOptions & { id: number; open: boolean };

const ToastContext = createContext<((options: ToastOptions) => void) | null>(null);

/** Show a toast from any client component under <Toaster>. */
export function useToast() {
  const toast = useContext(ToastContext);
  if (!toast) throw new Error("useToast must be used inside <Toaster>.");
  return toast;
}

const MAX_VISIBLE = 3;
const EXIT_MS = 300;

/**
 * Wraps the app once (root layout). Radix Toast handles the live-region announcement,
 * swipe to dismiss, pausing on hover/focus, and the F8 shortcut to reach the region.
 * Errors are `foreground` (announced assertively); everything else is `background`.
 */
export function Toaster({ children }: { children: ReactNode }) {
  const t = useTranslations("Toast");
  const [toasts, setToasts] = useState<ToastEntry[]>([]);
  const nextId = useRef(0);

  const toast = useCallback((options: ToastOptions) => {
    nextId.current += 1;
    const entry: ToastEntry = { ...options, id: nextId.current, open: true };
    setToasts((current) => [...current, entry].slice(-MAX_VISIBLE));
  }, []);

  const dismiss = useCallback((id: number) => {
    setToasts((current) => current.map((item) => (item.id === id ? { ...item, open: false } : item)));
    // Leave time for the exit animation before the element is removed.
    window.setTimeout(
      () => setToasts((current) => current.filter((item) => item.id !== id)),
      EXIT_MS,
    );
  }, []);

  return (
    <ToastPrimitive.Provider label={t("item")} swipeDirection="right" duration={5000}>
      <ToastContext.Provider value={toast}>{children}</ToastContext.Provider>

      {toasts.map((item) => (
        <ToastItem key={item.id} entry={item} onDismiss={() => dismiss(item.id)} />
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

const tones: Record<ToastVariant, { stripe: string; icon: ReactNode }> = {
  default: { stripe: "before:bg-estate", icon: <InfoCircleIcon className="text-estate" /> },
  success: { stripe: "before:bg-success", icon: <CheckCircleIcon className="text-success" /> },
  error: { stripe: "before:bg-danger", icon: <AlertCircleIcon className="text-danger" /> },
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
        "relative flex w-full items-start gap-3 overflow-hidden rounded-2xl border border-hairline bg-surface",
        "py-3.5 pr-14 pl-5 text-ink shadow-card",
        "before:absolute before:inset-y-0 before:left-0 before:w-1.5",
        tone.stripe,
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
          "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-estate",
        )}
      >
        <CloseIcon className="size-4.5" />
        <span className="sr-only">{t("close")}</span>
      </ToastPrimitive.Close>
    </ToastPrimitive.Root>
  );
}
