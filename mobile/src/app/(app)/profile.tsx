import { ProfileView } from "@/components/profile/profile-view";
import { Screen } from "@/components/screen";

/** Profile: the account, its giving, the phone's preferences and the ways in or out. */
export default function ProfileScreen() {
  return (
    <Screen edges={["top"]}>
      <ProfileView />
    </Screen>
  );
}
