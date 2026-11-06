import type { Metadata } from "next";
import { hasLocale, NextIntlClientProvider, useMessages, useTranslations } from "next-intl";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { InstitutionGate } from "@/components/bookings/InstitutionGate";
import { PaymentSettingsScreen } from "@/components/giving/PaymentSettingsScreen";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { routing } from "@/i18n/routing";
import { usePageLocale } from "@/i18n/static-locale";

export async function generateMetadata({ params }: PageProps<"/[locale]/giving/payments">): Promise<Metadata> {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  const t = await getTranslations({ locale, namespace: "Giving.payments" });
  return { title: t("title") };
}

/** Plan 09, M10: the institution's own PayHere merchant. */
export default function Page({ params }: PageProps<"/[locale]/giving/payments">) {
  usePageLocale(params);
  const t = useTranslations("Giving.payments");
  const messages = useMessages();

  return (
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "Giving", "Booking"])}>
      <header className="mb-8 grid gap-2">
        <h1 className="text-2xl font-semibold text-brand break-words sm:text-3xl">{t("title")}</h1>
        <p className="max-w-3xl text-muted">{t("intro")}</p>
      </header>
      <InstitutionGate permission="GIVING.PAYMENTS.VIEW">
        <PaymentSettingsScreen />
      </InstitutionGate>
    </NextIntlClientProvider>
  );
}
