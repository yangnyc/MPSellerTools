import { useEffect, useState, type ReactNode } from "react";
import { apiFetch } from "../lib/api";

import { AuthContext, type AuthState, type CurrentUser } from "./authState";

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
    setUser(me);
    setStatus("authenticated");
  };

  const logout = async () => {
    await apiFetch<void>("/api/auth/logout", { method: "POST" });
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

  return (
    <AuthContext.Provider value={{ user, status, login, logout, updateDisplayName }}>
      {children}
    </AuthContext.Provider>
  );
}
