import { NextIntlClientProvider, useMessages } from "next-intl";
import { Suspense, use } from "react";
import { InstitutionPage } from "@/components/bookings/InstitutionPage";
import { Spinner } from "@/components/ui/spinner";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { usePageLocale } from "@/i18n/static-locale";

/**
 * Plan 03, 5.7: an institution's own page, the link it shares (C10). Anyone may look at its meals
 * and open days; booking one asks for a sign-in. The name arrives in the browser, so the tab keeps
 * the brand's title.
 */
export default function Page({ params }: PageProps<"/[locale]/i/[slug]">) {
  usePageLocale(params);
  const { slug } = use(params);
  const messages = useMessages();

  return (
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "Discover", "Booking"])}>
      {/* The page reads ?slot=&date= (a donor back from signing in), which needs a boundary. */}
      <Suspense
        fallback={
          <div className="flex justify-center py-16 text-brand">
            <Spinner />
          </div>
        }
      >
        <InstitutionPage key={slug} slug={decodeURIComponent(slug)} />
      </Suspense>
    </NextIntlClientProvider>
  );
}
