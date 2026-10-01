import { notFound } from "next/navigation";

// Unknown paths under a locale render that locale's not-found page, not Next's bare default.
export default function CatchAll() {
  notFound();
}
