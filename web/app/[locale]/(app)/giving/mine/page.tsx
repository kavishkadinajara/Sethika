import type { Metadata } from "next";
import { hasLocale, NextIntlClientProvider, useMessages, useTranslations } from "next-intl";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { DonorGate } from "@/components/bookings/DonorGate";
import { MyGiving } from "@/components/giving/MyGiving";
import { MyPledges } from "@/components/giving/MyPledges";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { routing } from "@/i18n/routing";
import { usePageLocale } from "@/i18n/static-locale";

export async function generateMetadata({ params }: PageProps<"/[locale]/giving/mine">): Promise<Metadata> {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  const t = await getTranslations({ locale, namespace: "Giving.mine" });
  return { title: t("title") };
}

/** Plan 09, M18: the donor's own gifts and their receipts. */
export default function Page({ params }: PageProps<"/[locale]/giving/mine">) {
  usePageLocale(params);
  const t = useTranslations("Giving.mine");
  const messages = useMessages();

  return (
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "Giving", "Booking"])}>
      <header className="mb-8 grid gap-2">
        <h1 className="text-3xl font-semibold text-brand">{t("title")}</h1>
        <p className="max-w-3xl text-muted">{t("intro")}</p>
      </header>
      <DonorGate permission="MYGIVING.DONATIONS.VIEW">
        <div className="grid gap-8">
          <MyPledges />
          <MyGiving />
        </div>
      </DonorGate>
    </NextIntlClientProvider>
  );
}
