import createMiddleware from "next-intl/middleware";
import { routing } from "@/i18n/routing";

// Next.js 16 renamed middleware.ts to proxy.ts; next-intl's handler is a plain request handler,
// so it is exported as the proxy unchanged.
export default createMiddleware(routing);

export const config = {
  // Everything except the API (A3), Next internals and files with an extension.
  matcher: "/((?!api|trpc|_next|_vercel|.*\\..*).*)",
};
