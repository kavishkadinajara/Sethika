import type { Metadata } from "next";
import { hasLocale, NextIntlClientProvider, useMessages, useTranslations } from "next-intl";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { CalendarScreen } from "@/components/bookings/CalendarScreen";
import { InstitutionGate } from "@/components/bookings/InstitutionGate";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { routing } from "@/i18n/routing";
import { usePageLocale } from "@/i18n/static-locale";

export async function generateMetadata({ params }: PageProps<"/[locale]/bookings/calendar">): Promise<Metadata> {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  const t = await getTranslations({ locale, namespace: "Calendar" });
  return { title: t("title") };
}

/** Plan 03, 5.7: the institution's bookings by month, and the requests waiting for an answer. */
export default function Page({ params }: PageProps<"/[locale]/bookings/calendar">) {
  usePageLocale(params);
  const t = useTranslations("Calendar");
  const messages = useMessages();

  return (
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "Calendar", "Booking"])}>
      <header className="mb-8 grid gap-2">
        <h1 className="text-3xl font-semibold text-brand">{t("title")}</h1>
        <p className="max-w-3xl text-muted">{t("intro")}</p>
      </header>
      <InstitutionGate permission="BOOKINGS.CALENDAR.VIEW">
        <CalendarScreen />
      </InstitutionGate>
    </NextIntlClientProvider>
  );
}
