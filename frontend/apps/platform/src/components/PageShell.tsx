import type { ReactNode } from "react";
import { AppPage } from "examples/Kit";
import { useAuth } from "../auth/useAuth";

// The frame around every signed-in page (top bar, content column, footer).
export default function PageShell({ children }: { children: ReactNode }) {
  const { user, logout } = useAuth();

  return (
    <AppPage user={user} onLogout={logout} consoleName="Platform Console">
      {children}
    </AppPage>
  );
}
