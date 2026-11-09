import type { AdminUser, PlatformRole } from "./types";

/*  The API's rank rule for suspending or reactivating an account (PlatformAdminHandler,
    `RankNotJunior`), read on the client so the screen offers only what the API will do (access
    audit, open item "platform-rank UX mismatch", fixed 2026-11-08). The API still decides.

    An account with no platform role is anyone's to suspend. One with platform roles is the
    caller's only when the caller outranks its most senior one - that is, when the caller may
    manage every role it holds. A role the list does not know (not loaded yet, or retired) counts
    as senior: the button would only lead to a refusal.                                        */

/** The caller outranks every platform role <user> holds, so the API lets them suspend or reactivate it. */
export function outranksAccount(user: Pick<AdminUser, "platformRoles">, roles: readonly PlatformRole[]): boolean {
  return user.platformRoles.every((code) => roles.find((r) => r.code === code)?.canManage === true);
}
