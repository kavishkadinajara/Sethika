import type { Metadata } from "next";
import { hasLocale, NextIntlClientProvider, useMessages, useTranslations } from "next-intl";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { InstitutionGate } from "@/components/bookings/InstitutionGate";
import { LeadersScreen } from "@/components/services/LeadersScreen";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { routing } from "@/i18n/routing";
import { usePageLocale } from "@/i18n/static-locale";

export async function generateMetadata({ params }: PageProps<"/[locale]/services/leaders">): Promise<Metadata> {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  const t = await getTranslations({ locale, namespace: "Services.leaders" });
  return { title: t("title") };
}

/** Phase E, 8.9: the institution's religious leaders, their usual week and their leave (promise 3). */
export default function Page({ params }: PageProps<"/[locale]/services/leaders">) {
  usePageLocale(params);
  const t = useTranslations("Services.leaders");
  const messages = useMessages();

  return (
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "Services", "Booking", "Discover"])}>
      <header className="mb-8 grid gap-2">
        <h1 className="text-3xl font-semibold text-brand">{t("title")}</h1>
        <p className="max-w-3xl text-muted">{t("intro")}</p>
      </header>
      <InstitutionGate permission="SERVICES.LEADERS.VIEW">
        <LeadersScreen />
      </InstitutionGate>
    </NextIntlClientProvider>
  );
}
