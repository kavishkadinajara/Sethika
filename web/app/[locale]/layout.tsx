import type { Metadata } from "next";
import { hasLocale, NextIntlClientProvider } from "next-intl";
import { getTranslations, setRequestLocale } from "next-intl/server";
import { notFound } from "next/navigation";
import { PermissionProvider } from "@/components/permissions/PermissionProvider";
import { routing } from "@/i18n/routing";
import { fontVariables } from "@/lib/fonts";
import { loadEffectivePermissions } from "@/lib/permissions/server";
import "../globals.css";

export async function generateMetadata({ params }: LayoutProps<"/[locale]">): Promise<Metadata> {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();

  const t = await getTranslations({ locale });
  return {
    title: t("Metadata.title"),
    description: t("Brand.tagline"),
  };
}

export const dynamic = "force-dynamic";

export default async function LocaleLayout({ children, params }: LayoutProps<"/[locale]">) {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();

  setRequestLocale(locale);
  const effective = await loadEffectivePermissions();

  return (
    <html lang={locale} className={`${fontVariables} h-full antialiased`}>
      <body className="min-h-full flex flex-col bg-ivory text-ink">
        <NextIntlClientProvider>
          <PermissionProvider effective={effective}>{children}</PermissionProvider>
        </NextIntlClientProvider>
      </body>
    </html>
  );
}
