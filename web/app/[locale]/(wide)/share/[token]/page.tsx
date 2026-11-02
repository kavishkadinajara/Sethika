import type { Metadata } from "next";
import { hasLocale, NextIntlClientProvider } from "next-intl";
import { getMessages, getTranslations, setRequestLocale } from "next-intl/server";
import { notFound } from "next/navigation";
import { ShareScreen } from "@/components/bookings/ShareScreen";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { routing } from "@/i18n/routing";

export async function generateMetadata({ params }: PageProps<"/[locale]/share/[token]">): Promise<Metadata> {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  const t = await getTranslations({ locale, namespace: "Share" });
  // The link is a bearer secret (H16): never indexed, never sent on as a referrer.
  return { title: t("title"), robots: { index: false, follow: false }, referrer: "no-referrer" };
}

/** Plan 07, H17: a co-sponsor's link - the booking's summary, then join or decline. */
export default async function Page({ params }: PageProps<"/[locale]/share/[token]">) {
  const { locale, token } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  setRequestLocale(locale);
  const messages = await getMessages();

  return (
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "Share", "Booking"])}>
      <ShareScreen token={decodeURIComponent(token)} />
    </NextIntlClientProvider>
  );
}
