import type { Metadata } from "next";
import { hasLocale, NextIntlClientProvider, useMessages, useTranslations } from "next-intl";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { InstitutionGate } from "@/components/bookings/InstitutionGate";
import { SponsorsScreen } from "@/components/sponsors/SponsorsScreen";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { routing } from "@/i18n/routing";
import { usePageLocale } from "@/i18n/static-locale";

export async function generateMetadata({ params }: PageProps<"/[locale]/bookings/sponsors">): Promise<Metadata> {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  const t = await getTranslations({ locale, namespace: "Sponsors" });
  return { title: t("title") };
}

/** Plan 08: the institution's regular donors and, from K.2, their standing sponsorships. */
export default function Page({ params }: PageProps<"/[locale]/bookings/sponsors">) {
  usePageLocale(params);
  const t = useTranslations("Sponsors");
  const messages = useMessages();

  return (
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "Sponsors", "Booking"])}>
      <header className="mb-8 grid gap-2">
        {/* "Regular donors" in Tamil is one word wider than a 320 px phone: it may break inside. */}
        <h1 className="text-3xl font-semibold text-brand [overflow-wrap:anywhere]">{t("title")}</h1>
        <p className="max-w-3xl text-muted">{t("intro")}</p>
      </header>
      <InstitutionGate permission="BOOKINGS.SPONSORS.VIEW">
        <SponsorsScreen />
      </InstitutionGate>
    </NextIntlClientProvider>
  );
}
