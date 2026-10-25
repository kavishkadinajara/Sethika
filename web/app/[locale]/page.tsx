import { NextIntlClientProvider, useMessages, useTranslations } from "next-intl";
import { textLink } from "@/components/auth/links";
import { SessionPanel } from "@/components/auth/SessionPanel";
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

        <p className="mb-8 flex flex-wrap gap-x-6">
          <Link href="/institutions" className={cn(textLink, focusRing)}>
            {t("Home.find")}
          </Link>
          <Can permission="MYBOOKINGS.BOOKINGS.VIEW">
            <Link href="/bookings/mine" className={cn(textLink, focusRing)}>
              {t("Home.mine")}
            </Link>
          </Can>
          <Can permission="BOOKINGS.CALENDAR.VIEW">
            <Link href="/bookings/calendar" className={cn(textLink, focusRing)}>
              {t("Home.calendar")}
            </Link>
          </Can>
          <Can permission="BOOKINGS.SLOTS.VIEW">
            <Link href="/bookings/slots" className={cn(textLink, focusRing)}>
              {t("Home.slots")}
            </Link>
          </Can>
        </p>

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

        <PermissionDebugPanel />
      </main>
    </NextIntlClientProvider>
  );
}
