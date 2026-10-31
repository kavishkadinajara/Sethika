import type { Metadata } from "next";
import { hasLocale, NextIntlClientProvider, useMessages } from "next-intl";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { use } from "react";
import { ReviewGate } from "@/components/review/ReviewGate";
import { ReviewScreen } from "@/components/review/ReviewScreen";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { routing } from "@/i18n/routing";
import { usePageLocale } from "@/i18n/static-locale";

export async function generateMetadata({ params }: PageProps<"/[locale]/admin/registrations/[requestNo]">): Promise<Metadata> {
  const { locale, requestNo } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  const t = await getTranslations({ locale, namespace: "Review" });
  return { title: t("detail.requestNo", { requestNo: decodeURIComponent(requestNo) }) };
}

/** Plan 02, 5.6: one application, reviewed and decided. */
export default function Page({ params }: PageProps<"/[locale]/admin/registrations/[requestNo]">) {
  usePageLocale(params);
  const { requestNo } = use(params);
  const messages = useMessages();

  return (
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "Apply", "Review"])}>
      <ReviewGate>
        <ReviewScreen key={requestNo} requestNo={decodeURIComponent(requestNo)} />
      </ReviewGate>
    </NextIntlClientProvider>
  );
}
