import type { NextConfig } from "next";
import createNextIntlPlugin from "next-intl/plugin";

const withNextIntl = createNextIntlPlugin("./i18n/request.ts");

/*  A3: the browser only ever talks to this origin; /api/* is proxied to the API from the
    server. Same-origin keeps the refresh cookie SameSite=Lax and needs no CORS.
    API_BASE_URL is server-only (no NEXT_PUBLIC_): the browser never needs the API's address,
    and a NEXT_PUBLIC_ value would be baked into the client bundle for nothing.
    The default is the API's `http` launch profile (launchSettings.json).                  */
const apiBaseUrl = (process.env.API_BASE_URL ?? "http://localhost:5182").replace(/\/+$/, "");

if (!process.env.API_BASE_URL && process.env.NODE_ENV === "production") {
  console.warn(`[sethika] API_BASE_URL is not set; /api/* will be proxied to ${apiBaseUrl}.`);
}

const nextConfig: NextConfig = {
  // Development only: the "N" indicator's default corner (bottom left) covers the sidebar's fold
  // button (plan 05, R2).
  devIndicators: { position: "bottom-right" },
  async rewrites() {
    return [{ source: "/api/:path*", destination: `${apiBaseUrl}/api/:path*` }];
  },
};

export default withNextIntl(nextConfig);
