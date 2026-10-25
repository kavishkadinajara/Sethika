import type { Metadata } from "next";
import { hasLocale, NextIntlClientProvider, useMessages, useTranslations } from "next-intl";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { DonorGate } from "@/components/bookings/DonorGate";
import { MyBookings } from "@/components/bookings/MyBookings";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { routing } from "@/i18n/routing";
import { usePageLocale } from "@/i18n/static-locale";

export async function generateMetadata({ params }: PageProps<"/[locale]/bookings/mine">): Promise<Metadata> {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  const t = await getTranslations({ locale, namespace: "MyBookings" });
  return { title: t("title") };
}

/** Plan 03, 5.7: the donor's own bookings, and cancelling one while it may still be cancelled. */
export default function Page({ params }: PageProps<"/[locale]/bookings/mine">) {
  usePageLocale(params);
  const t = useTranslations("MyBookings");
  const messages = useMessages();

  return (
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "MyBookings", "Booking", "Discover"])}>
      <header className="mb-8 grid gap-2">
        <h1 className="text-3xl font-semibold text-brand">{t("title")}</h1>
        <p className="max-w-3xl text-muted">{t("intro")}</p>
      </header>
      <DonorGate permission="MYBOOKINGS.BOOKINGS.VIEW">
        <MyBookings />
      </DonorGate>
    </NextIntlClientProvider>
  );
}
