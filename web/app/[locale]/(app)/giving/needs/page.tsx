import type { Metadata } from "next";
import { hasLocale, NextIntlClientProvider, useMessages, useTranslations } from "next-intl";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { InstitutionGate } from "@/components/bookings/InstitutionGate";
import { NeedsScreen } from "@/components/giving/NeedsScreen";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { routing } from "@/i18n/routing";
import { usePageLocale } from "@/i18n/static-locale";

export async function generateMetadata({ params }: PageProps<"/[locale]/giving/needs">): Promise<Metadata> {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  const t = await getTranslations({ locale, namespace: "Giving.needs" });
  return { title: t("title") };
}

/** Plan 09, D.2: the institution's needs, opened onto the public board, and the pledges on them. */
export default function Page({ params }: PageProps<"/[locale]/giving/needs">) {
  usePageLocale(params);
  const t = useTranslations("Giving.needs");
  const messages = useMessages();

  return (
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "Giving", "Booking"])}>
      <header className="mb-8 grid gap-2">
        <h1 className="text-2xl font-semibold text-brand break-words sm:text-3xl">{t("title")}</h1>
        <p className="max-w-3xl text-muted">{t("intro")}</p>
      </header>
      <InstitutionGate permission="GIVING.NEEDS.VIEW">
        <NeedsScreen />
      </InstitutionGate>
    </NextIntlClientProvider>
  );
}
