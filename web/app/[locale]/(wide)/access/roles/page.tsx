import type { Metadata } from "next";
import { hasLocale, NextIntlClientProvider, useMessages, useTranslations } from "next-intl";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { RolesScreen } from "@/components/access/RolesScreen";
import { InstitutionGate } from "@/components/bookings/InstitutionGate";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { routing } from "@/i18n/routing";
import { usePageLocale } from "@/i18n/static-locale";

export async function generateMetadata({ params }: PageProps<"/[locale]/access/roles">): Promise<Metadata> {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  const t = await getTranslations({ locale, namespace: "Access.roles" });
  return { title: t("title") };
}

/** Plan 04, U3: the institution's roles and what each may do. */
export default function Page({ params }: PageProps<"/[locale]/access/roles">) {
  usePageLocale(params);
  const t = useTranslations("Access.roles");
  const messages = useMessages();

  return (
    // Booking for the gate's words.
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "Access", "Booking"])}>
      <header className="mb-8 grid gap-2">
        <h1 className="text-3xl font-semibold text-brand">{t("title")}</h1>
        <p className="max-w-3xl text-muted">{t("intro")}</p>
      </header>
      <InstitutionGate permission="ACCESS.ROLES.VIEW">
        <RolesScreen />
      </InstitutionGate>
    </NextIntlClientProvider>
  );
}
