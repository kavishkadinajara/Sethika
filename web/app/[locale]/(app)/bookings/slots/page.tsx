import type { Metadata } from "next";
import { hasLocale, NextIntlClientProvider, useMessages, useTranslations } from "next-intl";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { InstitutionGate } from "@/components/bookings/InstitutionGate";
import { SlotsScreen } from "@/components/bookings/SlotsScreen";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { routing } from "@/i18n/routing";
import { usePageLocale } from "@/i18n/static-locale";

export async function generateMetadata({ params }: PageProps<"/[locale]/bookings/slots">): Promise<Metadata> {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  const t = await getTranslations({ locale, namespace: "Slots" });
  return { title: t("title") };
}

/** Plan 03, 5.7: the institution's meal slots, the days it takes out, and its public listing. */
export default function Page({ params }: PageProps<"/[locale]/bookings/slots">) {
  usePageLocale(params);
  const t = useTranslations("Slots");
  const messages = useMessages();

  return (
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "Slots", "Booking", "Discover"])}>
      <header className="mb-8 grid gap-2">
        <h1 className="text-3xl font-semibold text-brand">{t("title")}</h1>
        <p className="max-w-3xl text-muted">{t("intro")}</p>
      </header>
      <InstitutionGate permission="BOOKINGS.SLOTS.VIEW">
        <SlotsScreen />
      </InstitutionGate>
    </NextIntlClientProvider>
  );
}
