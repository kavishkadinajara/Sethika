import { AppFrame } from "@/components/layout/AppFrame";
import { usePageLocale } from "@/i18n/static-locale";

/** The same frame, wide: an institution's page and the booking calendar want the room. */
export default function WideLayout({ children, params }: LayoutProps<"/[locale]">) {
  usePageLocale(params);
  return <AppFrame width="wide">{children}</AppFrame>;
}
