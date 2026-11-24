"use client";

import { useTranslations } from "next-intl";
import type { ReactNode } from "react";
import { PersonalSwitch } from "@/components/shell/PersonalSwitch";
import { Button } from "@/components/ui/button";
import { SheetBody } from "@/components/ui/sheet";
import { Spinner } from "@/components/ui/spinner";
import { Link } from "@/i18n/navigation";
import { useSession } from "@/lib/auth/useSession";
import { usePermissions } from "@/lib/permissions/usePermissions";

type DonorSheetGateProps = {
  /** Where signing in or signing up comes back to: the same page, with the choice already made. */
  back: string;
  /** What the donor's own session must hold to go on. */
  permission: string;
  /** Why to sign in, in this sheet's words. */
  signInFirst: string;
  /** What to do from an institution session, in this sheet's words. */
  institutionSession: string;
  /** Signed in, but the account lacks `permission`. */
  notAllowed: string;
  children: ReactNode;
};

/**
 * What a donor's sheet shows until it can be used: a sign-in that comes back to the same choice, a
 * switch out of an institution session (the donor permissions apply only in a personal one, plan 02,
 * B1), or why the account cannot. Asked only at the moment it is needed, never on opening the page.
 */
export function DonorSheetGate({ back, permission, signInFirst, institutionSession, notAllowed, children }: DonorSheetGateProps) {
  const t = useTranslations("Discover.sheet");
  const session = useSession();
  const { can, isLoading } = usePermissions();

  if (session.status === "loading" || (session.status === "authenticated" && isLoading)) {
    return (
      <div className="flex justify-center py-12 text-brand">
        <Spinner />
      </div>
    );
  }

  if (session.status !== "authenticated") {
    const next = `?next=${encodeURIComponent(back)}`;
    return (
      <SheetBody className="grid gap-4">
        <p className="text-fg">{signInFirst}</p>
        <div className="flex flex-wrap gap-3">
          <Button asChild>
            <Link href={`/login${next}`}>{t("signIn")}</Link>
          </Button>
          <Button asChild variant="outline">
            <Link href={`/signup${next}`}>{t("createAccount")}</Link>
          </Button>
        </div>
      </SheetBody>
    );
  }

  if (session.activeInstitution)
    return (
      <SheetBody className="text-fg">
        <p>{institutionSession}</p>
        <PersonalSwitch className="mt-4" />
      </SheetBody>
    );
  if (!can(permission)) return <SheetBody className="text-fg">{notAllowed}</SheetBody>;

  return <>{children}</>;
}
