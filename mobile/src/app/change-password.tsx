import { ChangePasswordForm } from "@/components/profile/change-password-form";
import { Screen } from "@/components/screen";

/** Opened from Profile, signed in only. The stack's header holds the way back. */
export default function ChangePasswordScreen() {
  return (
    <Screen edges={["bottom"]}>
      <ChangePasswordForm />
    </Screen>
  );
}
