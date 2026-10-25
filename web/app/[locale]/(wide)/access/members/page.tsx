import type { Metadata } from "next";
import { hasLocale, NextIntlClientProvider, useMessages, useTranslations } from "next-intl";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { MembersScreen } from "@/components/access/MembersScreen";
import { InstitutionGate } from "@/components/bookings/InstitutionGate";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { routing } from "@/i18n/routing";
import { usePageLocale } from "@/i18n/static-locale";

export async function generateMetadata({ params }: PageProps<"/[locale]/access/members">): Promise<Metadata> {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  const t = await getTranslations({ locale, namespace: "Access.members" });
  return { title: t("title") };
}

/** Plan 04, U3: the institution's staff, their roles, and inviting someone new. */
export default function Page({ params }: PageProps<"/[locale]/access/members">) {
  usePageLocale(params);
  const t = useTranslations("Access.members");
  const messages = useMessages();

  return (
    // Booking for the gate's words and the dates in words.
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "Access", "Booking"])}>
      <header className="mb-8 grid gap-2">
        <h1 className="text-3xl font-semibold text-brand">{t("title")}</h1>
        <p className="max-w-3xl text-muted">{t("intro")}</p>
      </header>
      <InstitutionGate permission="ACCESS.MEMBERS.VIEW">
        <MembersScreen />
      </InstitutionGate>
    </NextIntlClientProvider>
  );
}
