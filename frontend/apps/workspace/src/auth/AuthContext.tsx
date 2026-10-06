import { useEffect, useState, type ReactNode } from "react";
import { apiFetch, resetAntiforgeryToken } from "../lib/api";

import { AuthContext, type AuthState, type CurrentUser, type ThemeSettings } from "./authState";

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<CurrentUser | null>(null);
  const [status, setStatus] = useState<AuthState["status"]>("loading");

  useEffect(() => {
    apiFetch<CurrentUser>("/api/auth/me")
      .then((me) => {
        setUser(me);
        setStatus("authenticated");
      })
      .catch(() => setStatus("anonymous"));
  }, []);

  const login = async (email: string, password: string) => {
    const me = await apiFetch<CurrentUser>("/api/auth/login", {
      method: "POST",
      body: JSON.stringify({ email, password }),
    });
    resetAntiforgeryToken();
    setUser(me);
    setStatus("authenticated");
  };

  const logout = async () => {
    await apiFetch<void>("/api/auth/logout", { method: "POST" });
    resetAntiforgeryToken();
    setUser(null);
    setStatus("anonymous");
  };

  const updateDisplayName = async (displayName: string) => {
    const me = await apiFetch<CurrentUser>("/api/auth/me", {
      method: "PUT",
      body: JSON.stringify({ displayName }),
    });
    setUser(me);
  };

  const saveTheme = async (theme: ThemeSettings) => {
    const me = await apiFetch<CurrentUser>("/api/auth/me/theme", {
      method: "PUT",
      body: JSON.stringify(theme),
    });
    setUser(me);
  };

  const loadTheme = async (themeName: string, darkMode?: boolean) => {
    const mode = darkMode === undefined ? "" : `?dark=${darkMode}`;
    const saved = await apiFetch<ThemeSettings | undefined>(`/api/auth/me/theme/${encodeURIComponent(themeName)}${mode}`);
    return saved && typeof saved === "object" && !Array.isArray(saved) ? saved : null;
  };

  // Shown at once and saved behind it; a save that fails puts the old pins back.
  const savePinnedMenus = async (menus: string[]) => {
    const before = user?.pinnedMenus ?? [];
    setUser((current) => (current ? { ...current, pinnedMenus: menus } : current));
    try {
      const me = await apiFetch<CurrentUser>("/api/auth/me/pinned-menus", {
        method: "PUT",
        body: JSON.stringify({ menus }),
      });
      setUser(me);
    } catch (err) {
      setUser((current) => (current ? { ...current, pinnedMenus: before } : current));
      throw err;
    }
  };

  return (
    <AuthContext.Provider
      value={{ user, status, login, logout, updateDisplayName, saveTheme, loadTheme, savePinnedMenus }}
    >
      {children}
    </AuthContext.Provider>
  );
}
