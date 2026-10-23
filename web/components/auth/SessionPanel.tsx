"use client";

import { useTranslations } from "next-intl";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Spinner } from "@/components/ui/spinner";
import { Link } from "@/i18n/navigation";
import { session } from "@/lib/auth";
import { useSession } from "@/lib/auth/useSession";

/** The session at a glance on the home page, with the controls a walk-through needs. */
export function SessionPanel() {
  const t = useTranslations("Session");
  const state = useSession();
  const [busy, setBusy] = useState<"logout" | "logoutAll" | null>(null);

  return (
    <Card className="mb-10">
      <CardHeader>
        <CardTitle as="h2">{t("title")}</CardTitle>
      </CardHeader>
      <CardContent className="grid gap-4">
        {state.status === "loading" && (
          <span className="text-brand">
            <Spinner />
          </span>
        )}

        {(state.status === "anonymous" || state.status === "unavailable") && (
          <>
            <p className="text-muted">{state.status === "unavailable" ? t("unavailable") : t("signedOut")}</p>
            <div className="flex flex-wrap gap-3">
              <Button asChild>
                <Link href="/login">{t("signIn")}</Link>
              </Button>
              <Button asChild variant="outline">
                <Link href="/signup">{t("signUp")}</Link>
              </Button>
            </div>
          </>
        )}

        {state.status === "authenticated" && (
          <>
            <p>{t("signedIn")}</p>
            {state.activeInstitution && (
              <p className="text-muted">
                {t("actingFor", {
                  name: state.institutions.find((i) => i.slug === state.activeInstitution)?.name ?? state.activeInstitution,
                })}
              </p>
            )}
            <div className="flex flex-wrap gap-3">
              {state.institutions.length > 1 && (
                <Button asChild variant="outline">
                  <Link href="/select-institution">{t("switchInstitution")}</Link>
                </Button>
              )}
              <Button asChild variant="outline">
                <Link href="/account/password">{t("changePassword")}</Link>
              </Button>
              <Button
                variant="outline"
                loading={busy === "logout"}
                onClick={async () => {
                  setBusy("logout");
                  await session.logout();
                  setBusy(null);
                }}
              >
                {t("signOut")}
              </Button>
              <Button
                variant="ghost"
                loading={busy === "logoutAll"}
                onClick={async () => {
                  setBusy("logoutAll");
                  await session.logoutAll();
                  setBusy(null);
                }}
              >
                {t("signOutEverywhere")}
              </Button>
            </div>
          </>
        )}
      </CardContent>
    </Card>
  );
}
