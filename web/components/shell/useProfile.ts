"use client";

import { useEffect, useSyncExternalStore } from "react";
import { session } from "@/lib/auth";
import { loadProfile, type Profile } from "@/lib/auth/api";
import { useSession } from "@/lib/auth/useSession";

/*  The signed-in user's name, read once per sign-in and shared by every component that asks
    (the account menu, the greeting). Module state like the session itself, cleared on sign-out
    so the next person on the same browser never sees it.                                    */
let current: Profile | null = null;
let loading = false;
const listeners = new Set<() => void>();

function publish(profile: Profile | null) {
  current = profile;
  listeners.forEach((listener) => listener());
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

export function useProfile(): Profile | null {
  const signedIn = useSession().status === "authenticated";
  const profile = useSyncExternalStore(subscribe, () => current, () => null);

  useEffect(() => {
    if (!signedIn) {
      if (current) publish(null);
      return;
    }
    if (current || loading) return;
    loading = true;
    void loadProfile().then((loaded) => {
      loading = false;
      // Signed out while it was on its way: it belongs to nobody now.
      if (session.getState().status === "authenticated") publish(loaded);
    });
  }, [signedIn]);

  return signedIn ? profile : null;
}

/** "Kavishka Dinajara" → "KD"; one word → its first letter. Never more than two letters. */
export function initials(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) return "";
  const first = [...words[0]][0] ?? "";
  const last = words.length > 1 ? ([...words[words.length - 1]][0] ?? "") : "";
  return (first + last).toUpperCase();
}
