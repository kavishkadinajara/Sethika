import type { CatalogueModule, CataloguePermission } from "./types";

/*  The role editor's rules (plan 04, X7), framework-free so they are unit-tested. The API checks
    all of this again (plan 01, 5.9); these only stop the screen asking for what will be refused
    and keep a role from holding an action without the VIEW it needs.                           */

export type PermissionIndex = ReadonlyMap<string, CataloguePermission>;

export function indexCatalogue(catalogue: readonly CatalogueModule[]): PermissionIndex {
  return new Map(catalogue.flatMap((m) => m.screens.flatMap((s) => s.permissions.map((p) => [p.code, p] as const))));
}

/** `code` and everything it needs, transitively (auth.PermissionDependencies). */
export function requirementsOf(code: string, index: PermissionIndex): Set<string> {
  const found = new Set<string>();
  const visit = (next: string) => {
    if (found.has(next)) return;
    found.add(next);
    for (const required of index.get(next)?.requires ?? []) visit(required);
  };
  visit(code);
  return found;
}

/** `code` and everything that needs it, transitively: what must go when it goes. */
export function dependentsOf(code: string, index: PermissionIndex): Set<string> {
  const found = new Set<string>([code]);
  let grew = true;
  while (grew) {
    grew = false;
    for (const permission of index.values()) {
      if (!found.has(permission.code) && permission.requires.some((r) => found.has(r))) {
        found.add(permission.code);
        grew = true;
      }
    }
  }
  return found;
}

/**
 * Whether the caller may turn `code` on: it and every permission it pulls in must be grantable
 * (inside the level's ceiling and held by the caller) or already on the role. Turning one off is
 * always allowed: only what is added must be held (plan 01, 5.9).
 */
export function canTurnOn(code: string, selected: ReadonlySet<string>, grantable: ReadonlySet<string>, index: PermissionIndex): boolean {
  return [...requirementsOf(code, index)].every((c) => selected.has(c) || grantable.has(c));
}

/** The selection after ticking or unticking `code`, with its requirements or its dependents. */
export function toggle(selected: ReadonlySet<string>, code: string, on: boolean, index: PermissionIndex): Set<string> {
  const next = new Set(selected);
  if (on) for (const c of requirementsOf(code, index)) next.add(c);
  else for (const c of dependentsOf(code, index)) next.delete(c);
  return next;
}

/** What saving would add and remove, each sorted. */
export function changes(saved: readonly string[], selected: ReadonlySet<string>): { added: string[]; removed: string[] } {
  const before = new Set(saved);
  return {
    added: [...selected].filter((c) => !before.has(c)).sort(),
    removed: [...before].filter((c) => !selected.has(c)).sort(),
  };
}

/** The actions that appear anywhere in the catalogue, in its order: the editor's columns. */
export function actionsOf(catalogue: readonly CatalogueModule[]): string[] {
  const seen: string[] = [];
  for (const m of catalogue) for (const s of m.screens) for (const p of s.permissions) if (!seen.includes(p.action)) seen.push(p.action);
  return seen;
}

/**
 * A role code from its name, as RoleManagementHandler accepts one (^[A-Z][A-Z0-9_]{1,49}$):
 * "Kitchen helper" → "KITCHEN_HELPER". A name with no Latin letters gives "", and the form then
 * asks for a code.
 */
export function roleCodeFrom(name: string): string {
  const code = name
    .normalize("NFKD")
    // NFKD splits "ā" into "a" and a combining mark; the mark goes, the letter stays.
    .replace(/\p{M}/gu, "")
    .toUpperCase()
    .replace(/[^A-Z0-9]+/g, "_")
    .replace(/^[^A-Z]+/, "")
    .replace(/_+$/, "")
    .slice(0, 50)
    .replace(/_+$/, "");
  return /^[A-Z][A-Z0-9_]{1,49}$/.test(code) ? code : "";
}

export const isRoleCode = (code: string) => /^[A-Z][A-Z0-9_]{1,49}$/.test(code);
