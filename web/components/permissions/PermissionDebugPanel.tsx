"use client";

import { usePermissions } from "@/lib/permissions/usePermissions";

/**
 * Development aid: shows what the provider resolved, so a missing button can be traced to
 * a missing permission code rather than guessed at.
 */
export function PermissionDebugPanel() {
  const { isAuthenticated, isPlatform, institutionId, can } = usePermissions();

  const probes = [
    "BOOKINGS.CALENDAR.VIEW",
    "BOOKINGS.CALENDAR.APPROVE",
  ];

  return (
    <section className="mt-12 rounded border border-ink/10 bg-white p-4 text-sm">
      <h2 className="font-medium text-estate">Resolved context</h2>

      <dl className="mt-3 grid grid-cols-[auto_1fr] gap-x-4 gap-y-1">
        <dt className="text-ink/60">Signed in</dt>
        <dd>{isAuthenticated ? "yes" : "no"}</dd>

        <dt className="text-ink/60">Platform staff</dt>
        <dd>{isPlatform ? "yes" : "no"}</dd>

        <dt className="text-ink/60">Institution</dt>
        <dd>{institutionId ?? "none"}</dd>
      </dl>

      <ul className="mt-3 space-y-1">
        {probes.map((code) => (
          <li key={code} className="flex items-center gap-2">
            <span className={can(code) ? "text-estate" : "text-ink/40"}>
              {can(code) ? "granted" : "denied"}
            </span>
            <code className="text-ink/70">{code}</code>
          </li>
        ))}
      </ul>

      {!isAuthenticated && (
        <p className="mt-3 text-ink/60">
          Nothing issues an access token yet - login and JWT are separate Phase A work - so
          the app renders signed out and every gate is closed.
        </p>
      )}
    </section>
  );
}
