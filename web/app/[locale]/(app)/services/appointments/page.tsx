import type { Metadata } from "next";
import { hasLocale, NextIntlClientProvider, useMessages, useTranslations } from "next-intl";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { InstitutionGate } from "@/components/bookings/InstitutionGate";
import { AppointmentsScreen } from "@/components/services/AppointmentsScreen";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { routing } from "@/i18n/routing";
import { usePageLocale } from "@/i18n/static-locale";

export async function generateMetadata({ params }: PageProps<"/[locale]/services/appointments">): Promise<Metadata> {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  const t = await getTranslations({ locale, namespace: "Services.agenda" });
  return { title: t("title") };
}

/** Phase E, 8.9: the institution's service appointments as a day-by-day agenda (V10). */
export default function Page({ params }: PageProps<"/[locale]/services/appointments">) {
  usePageLocale(params);
  const t = useTranslations("Services.agenda");
  const messages = useMessages();

  return (
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "Services", "Booking", "Discover"])}>
      <header className="mb-8 grid gap-2">
        <h1 className="text-3xl font-semibold text-brand">{t("title")}</h1>
        <p className="max-w-3xl text-muted">{t("intro")}</p>
      </header>
      <InstitutionGate permission="SERVICES.APPOINTMENTS.VIEW">
        <AppointmentsScreen />
      </InstitutionGate>
    </NextIntlClientProvider>
  );
}
