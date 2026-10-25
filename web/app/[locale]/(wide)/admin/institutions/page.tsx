import type { Metadata } from "next";
import { hasLocale, NextIntlClientProvider, useMessages, useTranslations } from "next-intl";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { AdminGate } from "@/components/admin/AdminGate";
import { InstitutionsScreen } from "@/components/admin/InstitutionsScreen";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { routing } from "@/i18n/routing";
import { usePageLocale } from "@/i18n/static-locale";

export async function generateMetadata({ params }: PageProps<"/[locale]/admin/institutions">): Promise<Metadata> {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  const t = await getTranslations({ locale, namespace: "Admin.institutions" });
  return { title: t("title") };
}

/** Plan 04, Y5: every institution, and the way into one as platform support (Y1). */
export default function Page({ params }: PageProps<"/[locale]/admin/institutions">) {
  usePageLocale(params);
  const t = useTranslations("Admin.institutions");
  const messages = useMessages();

  return (
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "Admin"])}>
      <header className="mb-8 grid gap-2">
        <h1 className="text-3xl font-semibold text-brand">{t("title")}</h1>
        <p className="max-w-3xl text-muted">{t("intro")}</p>
      </header>
      <AdminGate permission="PLATFORM.INSTITUTIONS.VIEW">
        <InstitutionsScreen />
      </AdminGate>
    </NextIntlClientProvider>
  );
}
