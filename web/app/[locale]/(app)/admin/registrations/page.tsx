import type { Metadata } from "next";
import { hasLocale, NextIntlClientProvider, useMessages, useTranslations } from "next-intl";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { ReviewGate } from "@/components/review/ReviewGate";
import { ReviewQueue } from "@/components/review/ReviewQueue";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { routing } from "@/i18n/routing";
import { usePageLocale } from "@/i18n/static-locale";

export async function generateMetadata({ params }: PageProps<"/[locale]/admin/registrations">): Promise<Metadata> {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  const t = await getTranslations({ locale, namespace: "Review" });
  return { title: t("title") };
}

/** Plan 02, 5.6: the reviewer's queue. */
export default function Page({ params }: PageProps<"/[locale]/admin/registrations">) {
  usePageLocale(params);
  const t = useTranslations("Review");
  const messages = useMessages();

  return (
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "Apply", "Review"])}>
      <header className="mb-8 grid gap-2">
        <h1 className="text-3xl font-semibold text-brand">{t("title")}</h1>
        <p className="max-w-3xl text-muted">{t("intro")}</p>
      </header>
      <ReviewGate>
        {/* The queue reads ?status= and ?page=, which are only known in the browser. */}
        <Suspense>
          <ReviewQueue />
        </Suspense>
      </ReviewGate>
    </NextIntlClientProvider>
  );
}
