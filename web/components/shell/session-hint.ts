import { CSRF_COOKIE } from "@/lib/auth/csrf";

/**
 * Runs in <head> before the first paint, beside the theme's boot script (ThemeScript). The home
 * page is prerendered as the public landing page, which a signed-in person should not see flash
 * before their dashboard. The readable CSRF cookie lives exactly as long as the refresh cookie
 * (AuthCookies.cs), so its presence marks <html data-session>, and the `session:` variant
 * (globals.css) swaps the landing for the dashboard's placeholder until the session is known. A
 * hint only: once the session answers, the page shows what it really is.
 */
export const sessionHintScript = `(function(){try{if(/(?:^|; )${CSRF_COOKIE}=[^;]/.test(document.cookie))document.documentElement.setAttribute("data-session","")}catch(e){}})()`;
