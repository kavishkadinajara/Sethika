import type { Metadata } from "next";
import { hasLocale, NextIntlClientProvider, useMessages, useTranslations } from "next-intl";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { InstitutionGate } from "@/components/bookings/InstitutionGate";
import { ReportScreen } from "@/components/giving/ReportScreen";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { routing } from "@/i18n/routing";
import { usePageLocale } from "@/i18n/static-locale";

export async function generateMetadata({ params }: PageProps<"/[locale]/reports">): Promise<Metadata> {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  const t = await getTranslations({ locale, namespace: "Giving.report" });
  return { title: t("title") };
}

/** Plan 09, D.4: the month's report for the committee, on screen, as a file and on paper. */
export default function Page({ params }: PageProps<"/[locale]/reports">) {
  usePageLocale(params);
  const t = useTranslations("Giving.report");
  const messages = useMessages();

  return (
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "Giving", "Booking"])}>
      <header className="mb-8 grid gap-2 print:hidden">
        <h1 className="text-2xl font-semibold text-brand break-words sm:text-3xl">{t("title")}</h1>
        <p className="max-w-3xl text-muted">{t("intro")}</p>
      </header>
      <InstitutionGate permission="GIVING.REPORTS.VIEW">
        <ReportScreen />
      </InstitutionGate>
    </NextIntlClientProvider>
  );
}
