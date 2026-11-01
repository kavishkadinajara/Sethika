import { hasLocale } from "next-intl";
import { notFound, permanentRedirect } from "next/navigation";
import { routing } from "@/i18n/routing";
import { localePath } from "@/lib/bookings/seo";

/** Plan 06, G11: the old list of institutions is now the search; links to it still arrive there. */
export default async function Page({ params }: PageProps<"/[locale]/institutions">) {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  permanentRedirect(localePath(locale, "/find"));
}
