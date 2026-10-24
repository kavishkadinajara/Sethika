import { createContext, useContext, useEffect, useSyncExternalStore, type ReactNode } from "react";

import { API_BASE_URL, APP_USER_AGENT } from "@/config";

import { secureTokenStore } from "./secure-token-store";
import { createSessionClient, type SessionClient, type SessionState } from "./session-client";

/** One client for the app's lifetime: the single-flight rule only holds if there is one. */
export const sessionClient: SessionClient = createSessionClient({
  fetch: (input, init) => fetch(input, init),
  baseUrl: API_BASE_URL,
  store: secureTokenStore,
  now: () => Date.now(),
  headers: { "User-Agent": APP_USER_AGENT },
});

const SessionContext = createContext<SessionClient>(sessionClient);

export function SessionProvider({ children }: { children: ReactNode }) {
  useEffect(() => {
    void sessionClient.start();
  }, []);

  return <SessionContext.Provider value={sessionClient}>{children}</SessionContext.Provider>;
}

export function useSessionClient(): SessionClient {
  return useContext(SessionContext);
}

export function useSession(): SessionState {
  const client = useSessionClient();
  return useSyncExternalStore(client.subscribe, client.getState, client.getState);
}
