import { NextIntlClientProvider, useMessages } from "next-intl";
import { HomeScreen } from "@/components/home/HomeScreen";
import { Landing } from "@/components/home/Landing";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { usePageLocale } from "@/i18n/static-locale";

/** Plan 04: the public landing page for visitors, the dashboard once signed in. */
export default function Home({ params }: PageProps<"/[locale]">) {
  usePageLocale(params);
  const messages = useMessages();

  return (
    // Booking for the dates in words (useDates) and the statuses, Calendar for deciding a request
    // in "Waiting for you", Shell for today's table in the sidebar's words, Access for the
    // invitations card.
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "Dashboard", "Calendar", "Shell", "Booking", "Access"])}>
      <HomeScreen landing={<Landing />} />
    </NextIntlClientProvider>
  );
}
