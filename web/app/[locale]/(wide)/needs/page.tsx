import type { Metadata } from "next";
import { hasLocale, NextIntlClientProvider, useMessages, useTranslations } from "next-intl";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { NeedsBoard } from "@/components/giving/NeedsBoard";
import { Spinner } from "@/components/ui/spinner";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { routing } from "@/i18n/routing";
import { usePageLocale } from "@/i18n/static-locale";
import { languageAlternates, localePath } from "@/lib/bookings/seo";

export async function generateMetadata({ params }: PageProps<"/[locale]/needs">): Promise<Metadata> {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();

  const t = await getTranslations({ locale, namespace: "Giving.board" });
  const brand = await getTranslations({ locale, namespace: "Metadata" });
  return {
    title: `${t("title")} · ${brand("title")}`,
    description: t("intro"),
    alternates: { canonical: localePath(locale, "/needs"), languages: languageAlternates("/needs") },
  };
}

/** Plan 09, M12: the public needs board. Anyone may look; pledging asks for a sign-in at that moment. */
export default function Page({ params }: PageProps<"/[locale]/needs">) {
  usePageLocale(params);
  const t = useTranslations("Giving.board");
  const messages = useMessages();

  return (
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "Giving", "Booking", "Discover"])}>
      <header className="mb-8 grid gap-2">
        <h1 className="text-3xl font-semibold text-brand">{t("title")}</h1>
        <p className="max-w-3xl text-muted">{t("intro")}</p>
      </header>
      {/* A donor back from signing in has ?need= in the address, which needs a boundary to read. */}
      <Suspense
        fallback={
          <div className="flex justify-center py-16 text-brand">
            <Spinner />
          </div>
        }
      >
        <NeedsBoard />
      </Suspense>
    </NextIntlClientProvider>
  );
}
