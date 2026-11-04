import type { Metadata } from "next";
import { hasLocale, NextIntlClientProvider, useMessages, useTranslations } from "next-intl";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { InstitutionGate } from "@/components/bookings/InstitutionGate";
import { DonationsScreen } from "@/components/giving/DonationsScreen";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { routing } from "@/i18n/routing";
import { usePageLocale } from "@/i18n/static-locale";

export async function generateMetadata({ params }: PageProps<"/[locale]/giving/donations">): Promise<Metadata> {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  const t = await getTranslations({ locale, namespace: "Giving.donations" });
  return { title: t("title") };
}

/** Plan 09, D.1: the institution's gifts and receipts, recorded at the counter and voided by a second person. */
export default function Page({ params }: PageProps<"/[locale]/giving/donations">) {
  usePageLocale(params);
  const t = useTranslations("Giving.donations");
  const messages = useMessages();

  return (
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "Giving", "Booking"])}>
      <header className="mb-8 grid gap-2">
        {/* "Donations and receipts" in Tamil has a word wider than a 320 px phone at 3xl: smaller there. */}
        <h1 className="text-2xl font-semibold text-brand break-words sm:text-3xl">{t("title")}</h1>
        <p className="max-w-3xl text-muted">{t("intro")}</p>
      </header>
      <InstitutionGate permission="GIVING.DONATIONS.VIEW">
        <DonationsScreen />
      </InstitutionGate>
    </NextIntlClientProvider>
  );
}
