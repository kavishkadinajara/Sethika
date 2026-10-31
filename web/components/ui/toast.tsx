"use client";

import { createContext, lazy, Suspense, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from "react";

export type ToastVariant = "default" | "success" | "error";

export type ToastOptions = {
  /** Already translated: pass t("..."), never a literal. */
  title: string;
  description?: string;
  variant?: ToastVariant;
  /** Milliseconds. Errors stay longer by default so they can be read. */
  duration?: number;
};

export type ToastEntry = ToastOptions & { id: number; open: boolean };

const ToastContext = createContext<((options: ToastOptions) => void) | null>(null);

/** Show a toast from any client component under <Toaster>. */
export function useToast() {
  const toast = useContext(ToastContext);
  if (!toast) throw new Error("useToast must be used inside <Toaster>.");
  return toast;
}

// Radix Toast is ~22 KB gzipped and most pages never show a toast, so it stays out of the
// first load: it is fetched when the browser is idle, or at the first toast if that is sooner.
const ToastRegion = lazy(() => import("./toast-region"));

const MAX_VISIBLE = 3;
const EXIT_MS = 300;

/** Wraps the app once (root layout). Holds the queue; the region renders it. */
export function Toaster({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<ToastEntry[]>([]);
  const [regionWanted, setRegionWanted] = useState(false);
  const nextId = useRef(0);

  useEffect(() => {
    const load = () => setRegionWanted(true);
    if ("requestIdleCallback" in window) {
      const id = window.requestIdleCallback(load, { timeout: 4000 });
      return () => window.cancelIdleCallback(id);
    }
    const id = setTimeout(load, 2000);
    return () => clearTimeout(id);
  }, []);

  const toast = useCallback((options: ToastOptions) => {
    nextId.current += 1;
    const entry: ToastEntry = { ...options, id: nextId.current, open: true };
    setToasts((current) => [...current, entry].slice(-MAX_VISIBLE));
    setRegionWanted(true);
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
    <ToastContext.Provider value={toast}>
      {children}
      {regionWanted && (
        <Suspense fallback={null}>
          <ToastRegion toasts={toasts} onDismiss={dismiss} />
        </Suspense>
      )}
    </ToastContext.Provider>
  );
}
