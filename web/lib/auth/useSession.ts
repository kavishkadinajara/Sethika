"use client";

import { useSyncExternalStore } from "react";
import { session, type SessionState } from "./index";

const serverState: SessionState = { status: "loading" };

/** The session as React state. The server always renders "loading"; the browser takes over. */
export function useSession(): SessionState {
  return useSyncExternalStore(session.subscribe, session.getState, () => serverState);
}
