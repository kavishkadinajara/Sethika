import { AppFrame } from "@/components/layout/AppFrame";
import { usePageLocale } from "@/i18n/static-locale";

/** The home page's frame: the shell at its widest, for the dashboard's cards side by side. */
export default function HomeLayout({ children, params }: LayoutProps<"/[locale]">) {
  usePageLocale(params);
  return <AppFrame width="full">{children}</AppFrame>;
}
