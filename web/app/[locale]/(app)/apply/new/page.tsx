import type { Metadata } from "next";
import { hasLocale, NextIntlClientProvider, useMessages } from "next-intl";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { ApplicationScreen } from "@/components/onboarding/ApplicationScreen";
import { ApplyGate } from "@/components/onboarding/ApplyGate";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { routing } from "@/i18n/routing";
import { usePageLocale } from "@/i18n/static-locale";

export async function generateMetadata({ params }: PageProps<"/[locale]/apply/new">): Promise<Metadata> {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  const t = await getTranslations({ locale, namespace: "Apply" });
  return { title: t("start") };
}

/** A new application: the details step, which creates the draft when it is saved. */
export default function Page({ params }: PageProps<"/[locale]/apply/new">) {
  usePageLocale(params);
  const messages = useMessages();

  return (
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "Apply"])}>
      <ApplyGate>
        <ApplicationScreen requestNo={null} />
      </ApplyGate>
    </NextIntlClientProvider>
  );
}
