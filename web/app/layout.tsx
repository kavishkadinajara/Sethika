import type { Metadata } from "next";
import { Outfit } from "next/font/google";
import { PermissionProvider } from "@/components/permissions/PermissionProvider";
import { loadEffectivePermissions } from "@/lib/permissions/server";
import "./globals.css";

const outfit = Outfit({
  variable: "--font-outfit",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: "Sethika",
  description: "Sponsor a meal at a temple, kovil, mosque, church or care home in Sri Lanka.",
};


export const dynamic = "force-dynamic";

export default async function RootLayout({ children }: LayoutProps<"/">) {
  const effective = await loadEffectivePermissions();

  return (
    <html lang="en" className={`${outfit.variable} h-full antialiased`}>
      <body className="min-h-full flex flex-col bg-ivory text-ink">
        <PermissionProvider effective={effective}>{children}</PermissionProvider>
      </body>
    </html>
  );
}
