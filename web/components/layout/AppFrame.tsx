import { NextIntlClientProvider, useMessages } from "next-intl";
import type { ReactNode } from "react";
import { AppShell, type shellWidths } from "@/components/shell/AppShell";
import { pickMessages, sharedClientNamespaces } from "@/i18n/pick";

/**
 * The frame of every screen that is more than a sign-in form: the app shell (plan 04, W1), with
 * the messages its client components need. A page's own provider inside replaces these for the
 * page's content, which never needs the shell's.
 */
export function AppFrame({ width, children }: { width: keyof typeof shellWidths; children: ReactNode }) {
  const messages = useMessages();

  return (
    <NextIntlClientProvider messages={pickMessages(messages, [...sharedClientNamespaces, "Shell"])}>
      <AppShell width={width}>{children}</AppShell>
    </NextIntlClientProvider>
  );
}
