import type { Metadata } from "next";
import { hasLocale, NextIntlClientProvider, useMessages, useTranslations } from "next-intl";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { use } from "react";
import { DonorGate } from "@/components/bookings/DonorGate";
import { PaymentReturn } from "@/components/giving/PaymentReturn";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { routing } from "@/i18n/routing";
import { usePageLocale } from "@/i18n/static-locale";

export async function generateMetadata({ params }: PageProps<"/[locale]/giving/return/[orderId]">): Promise<Metadata> {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  const t = await getTranslations({ locale, namespace: "Giving.return" });
  return { title: t("pageTitle") };
}

/** Plan 09, M9: where PayHere sends a donor back. */
export default function Page({ params }: PageProps<"/[locale]/giving/return/[orderId]">) {
  usePageLocale(params);
  const { orderId } = use(params);
  const t = useTranslations("Giving.return");
  const messages = useMessages();

  return (
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "Giving", "Booking"])}>
      <header className="mb-8 grid gap-2">
        <h1 className="text-3xl font-semibold text-brand">{t("pageTitle")}</h1>
      </header>
      <DonorGate permission="MYGIVING.DONATIONS.VIEW">
        <PaymentReturn orderId={orderId} />
      </DonorGate>
    </NextIntlClientProvider>
  );
}
