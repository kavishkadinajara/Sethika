import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { AppState } from "react-native";

import { useSession, useSessionClient } from "./session-provider";

/*  GET /api/me/permissions (F25), the twin of the web's PermissionProvider. Hiding something here
    is UX only: the API checks every call. Every gate stays closed until the answer for the session
    in hand is in, so a switch to another institution never opens on the last one's permissions.

    Read again when the session starts, ends or moves to another institution, and when the phone
    comes back to the app, so a role changed meanwhile shows without a restart. A read that fails
    keeps what this session already had: a lost signal must not take the Today tab away.        */

export type Permissions = {
  /** True once this session's permissions were read, or the first read failed. */
  ready: boolean;
  can: (code: string) => boolean;
};

type Loaded = { key: string; codes: string[] | null };

const NOTHING: Permissions = { ready: false, can: () => false };

const PermissionContext = createContext<Permissions>(NOTHING);

export function PermissionProvider({ children }: { children: ReactNode }) {
  const client = useSessionClient();
  const session = useSession();
  // Who the session acts as: an institution's slug, "" for their own account, null for nobody.
  const key = session.status === "authenticated" ? (session.activeInstitution ?? "") : null;
  const [loaded, setLoaded] = useState<Loaded | null>(null);
  const [returns, setReturns] = useState(0);

  useEffect(() => {
    const subscription = AppState.addEventListener("change", (state) => {
      if (state === "active") setReturns((count) => count + 1);
    });
    return () => subscription.remove();
  }, []);

  useEffect(() => {
    if (key === null) {
      // Signed out: the next account on this phone may act for the same institution.
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setLoaded(null);
      return;
    }

    let cancelled = false;
    void read(client.fetch).then((codes) => {
      if (cancelled) return;
      setLoaded((previous) => (codes === null && previous?.key === key ? previous : { key, codes }));
    });
    return () => {
      cancelled = true;
    };
  }, [client, key, returns]);

  const current = loaded !== null && loaded.key === key ? loaded : null;

  const value = useMemo<Permissions>(() => {
    if (current === null) return NOTHING;
    const held = new Set(current.codes ?? []);
    return { ready: true, can: (code) => held.has(code) };
  }, [current]);

  return <PermissionContext.Provider value={value}>{children}</PermissionContext.Provider>;
}

export function usePermissions(): Permissions {
  return useContext(PermissionContext);
}

async function read(fetch: (path: string) => Promise<Response>): Promise<string[] | null> {
  try {
    const response = await fetch("/api/me/permissions");
    const { permissions } = (await response.json()) as { permissions?: string[] };
    return Array.isArray(permissions) ? permissions : null;
  } catch {
    return null;
  }
}
