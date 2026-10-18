import type { Metadata } from "next";
import { hasLocale, NextIntlClientProvider, useMessages } from "next-intl";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { StepUpPage } from "@/components/auth/StepUpPage";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { routing } from "@/i18n/routing";
import { usePageLocale } from "@/i18n/static-locale";

export async function generateMetadata({ params }: PageProps<"/[locale]/stepup">): Promise<Metadata> {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  const t = await getTranslations({ locale, namespace: "StepUp" });
  return {
    title: t("title"),
    // A signed-in, per-person screen: nothing here for a search engine.
    robots: { index: false, follow: false },
  };
}

export default function Page({ params }: PageProps<"/[locale]/stepup">) {
  usePageLocale(params);
  const messages = useMessages();

  return (
    <NextIntlClientProvider messages={pickMessages(messages, sharedClientNamespaces)}>
      <StepUpPage />
    </NextIntlClientProvider>
  );
}
