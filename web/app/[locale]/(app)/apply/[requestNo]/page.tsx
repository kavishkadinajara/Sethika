import type { Metadata } from "next";
import { hasLocale, NextIntlClientProvider, useMessages } from "next-intl";
import { getTranslations } from "next-intl/server";
import { notFound } from "next/navigation";
import { use } from "react";
import { ApplicationScreen } from "@/components/onboarding/ApplicationScreen";
import { ApplyGate } from "@/components/onboarding/ApplyGate";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";
import { routing } from "@/i18n/routing";
import { usePageLocale } from "@/i18n/static-locale";

export async function generateMetadata({ params }: PageProps<"/[locale]/apply/[requestNo]">): Promise<Metadata> {
  const { locale, requestNo } = await params;
  if (!hasLocale(routing.locales, locale)) notFound();
  const t = await getTranslations({ locale, namespace: "Apply" });
  return { title: t("view.requestNo", { requestNo: decodeURIComponent(requestNo) }) };
}

/** One of the applicant's applications: the form while it is a draft, its status after. */
export default function Page({ params }: PageProps<"/[locale]/apply/[requestNo]">) {
  usePageLocale(params);
  const { requestNo } = use(params);
  const messages = useMessages();

  return (
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "Apply"])}>
      <ApplyGate>
        <div className="max-w-3xl">
          <ApplicationScreen key={requestNo} requestNo={decodeURIComponent(requestNo)} />
        </div>
      </ApplyGate>
    </NextIntlClientProvider>
  );
}
