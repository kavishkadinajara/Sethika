import type { Metadata, Viewport } from "next";
import { hasLocale, NextIntlClientProvider } from "next-intl";
import { getMessages, getTranslations, setRequestLocale } from "next-intl/server";
import { notFound } from "next/navigation";
import { SessionBoot } from "@/components/auth/SessionBoot";
import { StepUpHost } from "@/components/auth/StepUpHost";
import { PermissionProvider } from "@/components/permissions/PermissionProvider";
import { themeColors } from "@/components/theme/theme";
import { ThemeScript } from "@/components/theme/theme-script";
import { ThemeSync } from "@/components/theme/theme-sync";
import { Toaster } from "@/components/ui/toast";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { routing } from "@/i18n/routing";
import { fontVariables } from "@/lib/fonts";
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

// The phone's address bar follows the theme; ThemeSync overrides it for a saved choice.
export const viewport: Viewport = {
  themeColor: [
    { media: "(prefers-color-scheme: light)", color: themeColors.light },
    { media: "(prefers-color-scheme: dark)", color: themeColors.dark },
  ],
};

// Nothing here depends on the request: the session and permissions load in the browser
// (lib/auth), so every locale's pages can be prerendered.
export function generateStaticParams() {
  return routing.locales.map((locale) => ({ locale }));
}

export default async function LocaleLayout({ children, params }: LayoutProps<"/[locale]">) {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();

  setRequestLocale(locale);
  const messages = await getMessages();

  return (
    // suppressHydrationWarning: ThemeScript sets data-theme on <html> before React hydrates.
    <html lang={locale} className={`${fontVariables} h-full antialiased`} suppressHydrationWarning>
      <head>
        <ThemeScript />
      </head>
      <body className="min-h-full flex flex-col bg-canvas text-fg">
        <ThemeSync />
        {/* Only what client components need; a page that needs more wraps itself in its own provider. */}
        <NextIntlClientProvider messages={pickMessages(messages, sharedClientNamespaces)}>
          <Toaster>
            <SessionBoot />
            <StepUpHost />
            <PermissionProvider>{children}</PermissionProvider>
          </Toaster>
        </NextIntlClientProvider>
      </body>
    </html>
  );
}
