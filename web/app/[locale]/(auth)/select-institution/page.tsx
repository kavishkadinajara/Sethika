import type { Metadata } from "next";
import { hasLocale, NextIntlClientProvider, useMessages } from "next-intl";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { InstitutionPicker } from "@/components/auth/InstitutionPicker";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { routing } from "@/i18n/routing";
import { usePageLocale } from "@/i18n/static-locale";

export async function generateMetadata({ params }: PageProps<"/[locale]/select-institution">): Promise<Metadata> {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  const t = await getTranslations({ locale, namespace: "SelectInstitution" });
  return {
    title: t("title"),
    // A signed-in, per-person screen: nothing here for a search engine.
    robots: { index: false, follow: false },
  };
}

export default function Page({ params }: PageProps<"/[locale]/select-institution">) {
  usePageLocale(params);
  const messages = useMessages();

  return (
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "SelectInstitution"])}>
      <InstitutionPicker />
    </NextIntlClientProvider>
  );
}
