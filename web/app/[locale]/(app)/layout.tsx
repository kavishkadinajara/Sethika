import { AppFrame } from "@/components/layout/AppFrame";
import { usePageLocale } from "@/i18n/static-locale";

/**
 * The frame of the signed-in screens that are more than a sign-in form: wider than the auth
 * frame, for forms with many fields, and still a single column on a phone.
 */
export default function AppLayout({ children, params }: LayoutProps<"/[locale]">) {
  usePageLocale(params);
  return <AppFrame width="narrow">{children}</AppFrame>;
}
