import { createContext } from "react";

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
  updateDisplayName: (displayName: string) => Promise<void>;
};

export const AuthContext = createContext<AuthState | null>(null);

