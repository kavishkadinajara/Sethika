import type { Metadata } from "next";
import { hasLocale, NextIntlClientProvider, useMessages } from "next-intl";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { SignupFlow } from "@/components/auth/SignupFlow";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { routing } from "@/i18n/routing";
import { usePageLocale } from "@/i18n/static-locale";

export async function generateMetadata({ params }: PageProps<"/[locale]/signup">): Promise<Metadata> {
  const { locale } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  const t = await getTranslations({ locale, namespace: "Signup" });
  return {
    title: t("title"),
  };
}

export default function Page({ params }: PageProps<"/[locale]/signup">) {
  usePageLocale(params);
  const messages = useMessages();

  return (
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "Signup", "Password"])}>
      <SignupFlow />
    </NextIntlClientProvider>
  );
}
