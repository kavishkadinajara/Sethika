import type { Metadata } from "next";
import { hasLocale, NextIntlClientProvider, useMessages, useTranslations } from "next-intl";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { DonorGate } from "@/components/bookings/DonorGate";
import { MemorialPlanner } from "@/components/bookings/MemorialPlanner";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { routing } from "@/i18n/routing";
import { usePageLocale } from "@/i18n/static-locale";

export async function generateMetadata({ params }: PageProps<"/[locale]/memorial">): Promise<Metadata> {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  const t = await getTranslations({ locale, namespace: "Memorial" });
  return { title: t("title") };
}

/** Plan 07, H6: the donor's memorial plans and the days their faith remembers them on. */
export default function Page({ params }: PageProps<"/[locale]/memorial">) {
  usePageLocale(params);
  const t = useTranslations("Memorial");
  const messages = useMessages();

  return (
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "Memorial", "Booking"])}>
      <header className="mb-8 grid gap-2">
        <h1 className="text-3xl font-semibold text-brand">{t("title")}</h1>
        <p className="max-w-3xl text-muted">{t("intro")}</p>
      </header>
      <DonorGate permission="MYMEMORIAL.PLANS.VIEW">
        <MemorialPlanner />
      </DonorGate>
    </NextIntlClientProvider>
  );
}
