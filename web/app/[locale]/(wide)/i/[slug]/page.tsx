import type { Metadata } from "next";
import { hasLocale, NextIntlClientProvider } from "next-intl";
import { getMessages, getTranslations, setRequestLocale } from "next-intl/server";
import { notFound } from "next/navigation";
import { Suspense } from "react";
import { InstitutionPage } from "@/components/bookings/InstitutionPage";
import { Spinner } from "@/components/ui/spinner";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { routing } from "@/i18n/routing";
import { localName } from "@/lib/bookings/names";
import { institutionJsonLd, institutionPath, jsonLdText, languageAlternates, localePath, metaDescription } from "@/lib/bookings/seo";
import { getPublicInstitution } from "@/lib/bookings/server";
import { siteOrigin } from "@/lib/site";

/**
 * Plan 03, 5.7: an institution's own page, the link it shares (C10). Anyone may look at its meals
 * and open days; booking one asks for a sign-in.
 *
 * Plan 06, G9: rendered on the server, so a search engine reads the name, address, description,
 * meals and services in the HTML, with a title, a snippet, the page in all three languages and
 * schema.org data. The calendar and the sheets stay interactive in the browser. If the API cannot
 * answer the server, the page loads in the browser as before; an institution that does not exist
 * or is not active is a 404.
 */
export async function generateMetadata({ params }: PageProps<"/[locale]/i/[slug]">): Promise<Metadata> {
  const { locale, slug } = await params;
  if (!hasLocale(routing.locales, locale)) return {};

  const read = await getPublicInstitution(decodeURIComponent(slug));
  if (read.status !== "ok") return {};

  const institution = read.value;
  const t = await getTranslations({ locale, namespace: "Metadata" });
  const name = localName(locale, institution);
  const title = `${name} · ${t("title")}`;
  const description = metaDescription(institution.description, t("institution", { name, city: institution.city }));
  const path = institutionPath(institution.slug);

  return {
    title,
    description,
    alternates: { canonical: localePath(locale, path), languages: languageAlternates(path) },
    openGraph: {
      type: "website",
      siteName: t("title"),
      title,
      description,
      url: localePath(locale, path),
      locale: `${locale}_LK`,
    },
  };
}

export default async function Page({ params }: PageProps<"/[locale]/i/[slug]">) {
  const { locale, slug: raw } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  setRequestLocale(locale);

  const slug = decodeURIComponent(raw);
  const read = await getPublicInstitution(slug);
  if (read.status === "not_found") notFound();

  const messages = await getMessages();
  const institution = read.status === "ok" ? read.value : null;

  return (
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "Discover", "Booking", "Services"])}>
      {institution && (
        <script
          type="application/ld+json"
          dangerouslySetInnerHTML={{
            __html: jsonLdText(institutionJsonLd(institution, locale, siteOrigin() + localePath(locale, institutionPath(institution.slug)))),
          }}
        />
      )}
      {/* The page reads ?slot=&date= (a donor back from signing in), which needs a boundary. */}
      <Suspense
        fallback={
          <div className="flex justify-center py-16 text-brand">
            <Spinner />
          </div>
        }
      >
        <InstitutionPage key={slug} slug={slug} initial={institution} />
      </Suspense>
    </NextIntlClientProvider>
  );
}
