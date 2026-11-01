import type { Metadata } from "next";
import { hasLocale, NextIntlClientProvider, useMessages, useTranslations } from "next-intl";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { FindScreen } from "@/components/find/FindScreen";
import { Spinner } from "@/components/ui/spinner";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { routing } from "@/i18n/routing";
import { usePageLocale } from "@/i18n/static-locale";
import { languageAlternates, localePath } from "@/lib/bookings/seo";

export async function generateMetadata({ params }: PageProps<"/[locale]/find">): Promise<Metadata> {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();

  const t = await getTranslations({ locale, namespace: "Find" });
  const brand = await getTranslations({ locale, namespace: "Metadata" });
  return {
    title: `${t("title")} · ${brand("title")}`,
    description: t("intro"),
    alternates: { canonical: localePath(locale, "/find"), languages: languageAlternates("/find") },
  };
}

/**
 * Plan 06, G11: one place to look for somewhere to give - a day, near me, on the map or in a
 * district - with a month of open days, a list and a map. Anyone may search; booking asks for a
 * sign-in on the place's own page.
 */
export default function Page({ params }: PageProps<"/[locale]/find">) {
  usePageLocale(params);
  const t = useTranslations("Find");
  const messages = useMessages();

  return (
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "Find", "Booking"])}>
      <header className="mb-8 grid gap-2">
        <h1 className="text-3xl font-semibold text-brand">{t("title")}</h1>
        <p className="max-w-3xl text-muted">{t("intro")}</p>
      </header>
      {/* The search lives in the address, which needs a boundary to read. */}
      <Suspense
        fallback={
          <div className="flex justify-center py-16 text-brand">
            <Spinner />
          </div>
        }
      >
        <FindScreen />
      </Suspense>
    </NextIntlClientProvider>
  );
}
