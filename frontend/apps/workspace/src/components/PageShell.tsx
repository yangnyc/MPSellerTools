import type { ReactNode } from "react";
import { AppPage } from "examples/Kit";
import { useAuth } from "../auth/useAuth";
import { useSnackbar } from "./useSnackbar";

// The frame around every signed-in page (top bar, content column, footer).
export default function PageShell({ children }: { children: ReactNode }) {
  const { user, logout } = useAuth();
  const { notifications, unreadCount, markNotificationsRead, clearNotifications } = useSnackbar();

  return (
    <AppPage
      user={user}
      onLogout={logout}
      consoleName="Workspace"
      notifications={{
        items: notifications,
        unreadCount,
        onRead: markNotificationsRead,
        onClear: clearNotifications,
      }}
    >
      {children}
    </AppPage>
  );
}
