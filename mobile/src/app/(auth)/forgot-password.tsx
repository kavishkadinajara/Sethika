import { useLocalSearchParams } from "expo-router";

import { ForgotPasswordFlow } from "@/components/auth/forgot-password-flow";
import { Screen } from "@/components/screen";

/** A forgotten password. `next` rides along, so sign-in still knows where the donor was going. */
export default function ForgotPasswordScreen() {
  const { next } = useLocalSearchParams<{ next?: string }>();

  return (
    <Screen>
      <ForgotPasswordFlow next={next} />
    </Screen>
  );
}
