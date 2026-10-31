import type { Metadata } from "next";
import { hasLocale, NextIntlClientProvider, useMessages } from "next-intl";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { LoginForm } from "@/components/auth/LoginForm";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { routing } from "@/i18n/routing";
import { usePageLocale } from "@/i18n/static-locale";

export async function generateMetadata({ params }: PageProps<"/[locale]/login">): Promise<Metadata> {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  const t = await getTranslations({ locale, namespace: "Login" });
  return {
    title: t("title"),
  };
}

export default function Page({ params }: PageProps<"/[locale]/login">) {
  usePageLocale(params);
  const messages = useMessages();

  return (
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "Login"])}>
      <LoginForm />
    </NextIntlClientProvider>
  );
}
