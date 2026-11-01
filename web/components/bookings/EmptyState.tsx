"use client";

import type { ReactNode } from "react";
import { Bowl } from "./Bowl";

/** A quiet empty state: the bowl, waiting, and one line in Sethika's human voice. */
export function EmptyState({ children }: { children: ReactNode }) {
  return (
    <div className="grid justify-items-center gap-3 rounded-bowl border border-dashed border-hairline px-6 py-10 text-center">
      <Bowl fill={0} className="h-8 w-12 text-brand/60" />
      <p className="max-w-sm font-tagline text-lg italic text-muted">{children}</p>
    </div>
  );
}
