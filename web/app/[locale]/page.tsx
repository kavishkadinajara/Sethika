import { NextIntlClientProvider, useMessages, useTranslations } from "next-intl";
import { textLink } from "@/components/auth/links";
import { ApproveDemoButton, SessionPanel } from "@/components/auth/SessionPanel";
import { Can } from "@/components/permissions/Can";
import { PermissionDebugPanel } from "@/components/permissions/PermissionDebugPanel";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { Link } from "@/i18n/navigation";
import { usePageLocale } from "@/i18n/static-locale";
import { cn, focusRing } from "@/lib/utils";

export default function Home({ params }: PageProps<"/[locale]">) {
  usePageLocale(params);
  const t = useTranslations();
  const messages = useMessages();

  return (
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "Session"])}>
      <main className="mx-auto w-full max-w-3xl px-4 py-12">
        <header className="mb-10">
          <h1 className="text-3xl font-semibold text-brand">{t("Brand.name")}</h1>
          <p className="mt-2 text-sm text-muted">{t("Home.intro")}</p>
        </header>

        <SessionPanel />

        <Can permission="ONBOARDING.APPLICATION.CREATE">
          <p className="mb-8">
            <Link href="/apply" className={cn(textLink, focusRing)}>
              {t("Home.apply")}
            </Link>
          </p>
        </Can>

        <Can permission="ONBOARDING.REQUESTS.VIEW">
          <p className="mb-8">
            <Link href="/admin/registrations" className={cn(textLink, focusRing)}>
              {t("Home.review")}
            </Link>
          </p>
        </Can>

        <section className="space-y-4">
          <h2 className="text-lg font-medium text-brand">{t("Home.calendarHeading")}</h2>

          <Can
            permission="BOOKINGS.CALENDAR.VIEW"
            fallback={
              <p className="rounded border border-hairline bg-surface p-4 text-sm text-muted">
                {t("Home.calendarDenied")}
              </p>
            }
          >
            <div className="rounded border border-hairline bg-surface p-4">
              <p className="text-sm">{t("Home.calendarPlaceholder")}</p>

              <Can permission="BOOKINGS.CALENDAR.APPROVE">
                <ApproveDemoButton label={t("Home.approveBooking")} />
              </Can>
            </div>
          </Can>
        </section>

        <PermissionDebugPanel />
      </main>
    </NextIntlClientProvider>
  );
}
