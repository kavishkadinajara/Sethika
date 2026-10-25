import { createContext, useContext, useEffect, useState, type ReactNode } from "react";

import { useSession, useSessionClient } from "./session-provider";

/*  GET /api/me, read once per sign-in for every tab (the greeting on Explore, the Profile tab,
    the account's language), instead of each screen asking again. Signed out, it is null.     */

export type Profile = { fullName: string; displayName: string | null; preferredLanguage: string | null };

const ProfileContext = createContext<Profile | null>(null);

export function ProfileProvider({ children }: { children: ReactNode }) {
  const client = useSessionClient();
  const signedIn = useSession().status === "authenticated";
  const [profile, setProfile] = useState<Profile | null>(null);

  useEffect(() => {
    if (!signedIn) {
      // Signed out: forget the last account's name at once.
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setProfile(null);
      return;
    }
    let cancelled = false;
    client
      .fetch("/api/me")
      .then(async (response) => {
        if (!response.ok) return;
        const body = (await response.json()) as { fullName?: unknown; displayName?: unknown; preferredLanguage?: unknown };
        if (cancelled) return;
        setProfile({
          fullName: typeof body.fullName === "string" ? body.fullName : "",
          displayName: typeof body.displayName === "string" && body.displayName ? body.displayName : null,
          preferredLanguage: typeof body.preferredLanguage === "string" ? body.preferredLanguage : null,
        });
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
  }, [client, signedIn]);

  return <ProfileContext.Provider value={profile}>{children}</ProfileContext.Provider>;
}

export function useProfile(): Profile | null {
  return useContext(ProfileContext);
}

/** The name to greet someone by: the one they chose to be called, else their first name. */
export function greetingName(profile: Profile | null): string | null {
  if (!profile) return null;
  return profile.displayName ?? (profile.fullName.trim().split(/\s+/)[0] || null);
}

/** Up to two initials for an avatar, from the full name (any script). */
export function initials(profile: Profile | null): string {
  const words = (profile?.displayName ?? profile?.fullName ?? "").trim().split(/\s+/).filter(Boolean);
  return words
    .slice(0, 2)
    .map((word) => Array.from(word)[0] ?? "")
    .join("")
    .toLocaleUpperCase();
}
