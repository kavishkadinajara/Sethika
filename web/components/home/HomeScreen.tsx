"use client";

import type { ReactNode } from "react";
import { useSession } from "@/lib/auth/useSession";
import { Dashboard } from "./Dashboard";

/**
 * The home page is two pages: the landing for visitors, the dashboard once signed in. The landing
 * arrives prerendered (a server component passed in), so visitors get static HTML. Until the
 * session is known, a likely session (`data-session`, session-hint.ts) shows the dashboard's
 * placeholder instead, so a signed-in person never sees the landing flash first.
 */
export function HomeScreen({ landing }: { landing: ReactNode }) {
  const status = useSession().status;

  if (status === "authenticated") return <Dashboard />;
  if (status === "anonymous" || status === "unavailable") return landing;

  return (
    <>
      <div className="session:hidden">{landing}</div>
      <DashboardPlaceholder />
    </>
  );
}

/** The dashboard's shape while the session loads: no words to read, nothing to announce. */
function DashboardPlaceholder() {
  return (
    <div aria-hidden className="hidden gap-10 session:grid">
      <div className="h-44 animate-pulse rounded-bowl bg-subtle motion-reduce:animate-none" />
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
        {[0, 1, 2].map((i) => (
          <div key={i} className="h-24 animate-pulse rounded-bowl border border-hairline bg-surface motion-reduce:animate-none" />
        ))}
      </div>
    </div>
  );
}
