import type { ConfigContext, ExpoConfig } from "expo/config";

/*  app.json holds the app's configuration; this file only adds what must not be committed.

    Push on Android goes through Firebase Cloud Messaging, which needs the Firebase project's
    google-services.json in the native build. The repository is public, so the file is not in it:
    an EAS build gets it from the secret file variable GOOGLE_SERVICES_JSON (a path on the build
    machine); for a local native build, point the same variable at a copy outside the repository.
    Without it the app builds as before and only push stays off (src/notifications).          */
export default ({ config }: ConfigContext): ExpoConfig => {
  const googleServicesFile = process.env.GOOGLE_SERVICES_JSON;

  return {
    ...config,
    name: config.name ?? "Sethika",
    slug: config.slug ?? "sethika",
    android: { ...config.android, ...(googleServicesFile ? { googleServicesFile } : {}) },
  };
};
