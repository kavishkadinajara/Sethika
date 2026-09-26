import type { EffectivePermissions } from "./types";

const API_BASE = process.env.NEXT_PUBLIC_API_BASE_URL ?? "https://localhost:7001";

export async function fetchEffectivePermissions(
  accessToken?: string,
): Promise<EffectivePermissions | null> {
  const response = await fetch(`${API_BASE}/api/me/permissions`, {
    headers: accessToken ? { Authorization: `Bearer ${accessToken}` } : {},
    cache: "no-store",
    credentials: "include",
  });

  if (response.status === 401) {
    return null;
  }

  if (!response.ok) {
    throw new Error(
      `Failed to load permissions: ${response.status} ${response.statusText}`,
    );
  }

  return (await response.json()) as EffectivePermissions;
}
