import { cookies } from "next/headers";
import { fetchEffectivePermissions } from "./api";
import type { EffectivePermissions } from "./types";

export async function loadEffectivePermissions(): Promise<EffectivePermissions | null> {
  try {
    const store = await cookies();
    const accessToken = store.get("sethika_at")?.value;

    return await fetchEffectivePermissions(accessToken);
  } catch (error) {
    console.error("[permissions] could not load effective permissions", error);
    return null;
  }
}
