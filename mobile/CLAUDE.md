@AGENTS.md

# Sethika mobile — rules on top of the Expo notes above

- The app is a client of the Sethika API. No business rules here; if a screen needs data the API
  does not give, add it to the owning module's plan and build it there (PHASE-F-MOBILE.md §7).
- Sessions go through `src/auth/session-client.ts` only. The refresh token lives in
  `expo-secure-store` — never AsyncStorage, never logged, never in state.
- Signed-in calls use `useSessionClient().fetch(...)`; anonymous ones `src/api/http.ts`.
- Shared strings are copied from `web/messages`; keep en/si/ta keys identical (a test checks).
- Before calling work done: `npm run lint && npm run typecheck && npm test`.
