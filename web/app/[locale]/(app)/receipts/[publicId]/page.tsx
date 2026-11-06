import type { Metadata } from "next";
import { hasLocale, NextIntlClientProvider, useMessages } from "next-intl";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { use } from "react";
import { ReceiptScreen } from "@/components/giving/ReceiptScreen";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { routing } from "@/i18n/routing";
import { usePageLocale } from "@/i18n/static-locale";

export async function generateMetadata({ params }: PageProps<"/[locale]/receipts/[publicId]">): Promise<Metadata> {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  const t = await getTranslations({ locale, namespace: "Giving.receipt" });
  // A receipt names a person and a gift: never in a search index.
  return { title: t("pageTitle"), robots: { index: false, follow: false } };
}

/** Plan 09, M4: one printable receipt, for its institution or its donor. */
export default function Page({ params }: PageProps<"/[locale]/receipts/[publicId]">) {
  usePageLocale(params);
  const { publicId } = use(params);
  const messages = useMessages();

  return (
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "Giving", "Booking"])}>
      <ReceiptScreen publicId={publicId} />
    </NextIntlClientProvider>
  );
}
