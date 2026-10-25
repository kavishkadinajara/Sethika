import type { Metadata } from "next";
import { hasLocale, NextIntlClientProvider, useMessages, useTranslations } from "next-intl";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { ApplicationList } from "@/components/onboarding/ApplicationList";
import { ApplyGate } from "@/components/onboarding/ApplyGate";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { routing } from "@/i18n/routing";
import { usePageLocale } from "@/i18n/static-locale";

export async function generateMetadata({ params }: PageProps<"/[locale]/apply">): Promise<Metadata> {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  const t = await getTranslations({ locale, namespace: "Apply" });
  return { title: t("title") };
}

/** Plan 02, 5.5: the applicant's applications, and the way to start one. */
export default function Page({ params }: PageProps<"/[locale]/apply">) {
  usePageLocale(params);
  const t = useTranslations("Apply");
  const messages = useMessages();

  return (
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "Apply"])}>
      <header className="mb-8 grid gap-2">
        <h1 className="text-3xl font-semibold text-brand">{t("title")}</h1>
        <p className="max-w-3xl text-muted">{t("intro")}</p>
      </header>
      <ApplyGate>
        <ApplicationList />
      </ApplyGate>
    </NextIntlClientProvider>
  );
}
