import type { Metadata } from "next";
import { hasLocale, NextIntlClientProvider, useMessages, useTranslations } from "next-intl";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { InstitutionDirectory } from "@/components/bookings/InstitutionDirectory";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { routing } from "@/i18n/routing";
import { usePageLocale } from "@/i18n/static-locale";

export async function generateMetadata({ params }: PageProps<"/[locale]/institutions">): Promise<Metadata> {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  const t = await getTranslations({ locale, namespace: "Discover" });
  return { title: t("directoryTitle") };
}

/** Plan 03, 5.7: the institutions that chose to be listed (C10). Anyone may look. */
export default function Page({ params }: PageProps<"/[locale]/institutions">) {
  usePageLocale(params);
  const t = useTranslations("Discover");
  const messages = useMessages();

  return (
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "Discover"])}>
      <header className="mb-8 grid gap-2">
        <h1 className="text-3xl font-semibold text-brand">{t("directoryTitle")}</h1>
        <p className="max-w-3xl text-muted">{t("directoryIntro")}</p>
      </header>
      <InstitutionDirectory />
    </NextIntlClientProvider>
  );
}
