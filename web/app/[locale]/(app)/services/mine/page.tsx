import type { Metadata } from "next";
import { hasLocale, NextIntlClientProvider, useMessages, useTranslations } from "next-intl";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { DonorGate } from "@/components/bookings/DonorGate";
import { MyServices } from "@/components/services/MyServices";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { routing } from "@/i18n/routing";
import { usePageLocale } from "@/i18n/static-locale";

export async function generateMetadata({ params }: PageProps<"/[locale]/services/mine">): Promise<Metadata> {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  const t = await getTranslations({ locale, namespace: "Services.mine" });
  return { title: t("title") };
}

/** Phase E, 8.9: the donor's own service requests, and cancelling one until the day before (V13). */
export default function Page({ params }: PageProps<"/[locale]/services/mine">) {
  usePageLocale(params);
  const t = useTranslations("Services.mine");
  const messages = useMessages();

  return (
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "Services", "Booking", "Discover"])}>
      <header className="mb-8 grid gap-2">
        <h1 className="text-3xl font-semibold text-brand">{t("title")}</h1>
        <p className="max-w-3xl text-muted">{t("intro")}</p>
      </header>
      <DonorGate permission="MYSERVICES.APPOINTMENTS.VIEW">
        <MyServices />
      </DonorGate>
    </NextIntlClientProvider>
  );
}
