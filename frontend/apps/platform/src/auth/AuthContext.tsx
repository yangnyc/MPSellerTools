import { createContext, useEffect, useState, type ReactNode } from "react";
import { apiFetch } from "../lib/api";

export type CurrentUser = {
  id: string;
  email: string;
  displayName: string;
  roles: string[];
};

export type AuthState = {
  user: CurrentUser | null;
  status: "loading" | "authenticated" | "anonymous";
  login: (email: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
};

export const AuthContext = createContext<AuthState | null>(null);

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

  return <AuthContext.Provider value={{ user, status, login, logout }}>{children}</AuthContext.Provider>;
}
