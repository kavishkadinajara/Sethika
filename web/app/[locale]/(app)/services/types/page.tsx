import type { Metadata } from "next";
import { hasLocale, NextIntlClientProvider, useMessages, useTranslations } from "next-intl";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { InstitutionGate } from "@/components/bookings/InstitutionGate";
import { ServiceTypesScreen } from "@/components/services/ServiceTypesScreen";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { routing } from "@/i18n/routing";
import { usePageLocale } from "@/i18n/static-locale";

export async function generateMetadata({ params }: PageProps<"/[locale]/services/types">): Promise<Metadata> {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  const t = await getTranslations({ locale, namespace: "Services.types" });
  return { title: t("title") };
}

/** Phase E, 8.9: the religious services the institution offers (promise 1). */
export default function Page({ params }: PageProps<"/[locale]/services/types">) {
  usePageLocale(params);
  const t = useTranslations("Services.types");
  const messages = useMessages();

  return (
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "Services", "Booking", "Discover"])}>
      <header className="mb-8 grid gap-2">
        <h1 className="text-3xl font-semibold text-brand">{t("title")}</h1>
        <p className="max-w-3xl text-muted">{t("intro")}</p>
      </header>
      <InstitutionGate permission="SERVICES.TYPES.VIEW">
        <ServiceTypesScreen />
      </InstitutionGate>
    </NextIntlClientProvider>
  );
}
