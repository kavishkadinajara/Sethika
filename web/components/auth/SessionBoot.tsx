"use client";

import { useEffect } from "react";
import { session } from "@/lib/auth";

/**
 * Silent refresh at app boot (A2): the access token died with the last page, so ask for a new
 * one from the refresh cookie before anything that depends on identity renders. Renders nothing.
 */
export function SessionBoot() {
  useEffect(() => {
    void session.start();
  }, []);

  return null;
}
