import { createContext } from "react";

export type ThemeSettings = {
  // The named theme (see context/themes.js in the shared UI package). Absent
  // on profiles saved before themes had names.
  themeName?: string | null;
  darkMode: boolean;
  whiteSidenav: boolean;
  sidenavTint: string | null;
  sidenavColor: string;
  fixedNavbar: boolean;
};

export type CurrentUser = {
  id: string;
  email: string;
  displayName: string;
  roles: string[];
  theme: ThemeSettings | null;
};

export type AuthState = {
  user: CurrentUser | null;
  status: "loading" | "authenticated" | "anonymous";
  login: (email: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
  updateDisplayName: (displayName: string) => Promise<void>;
  saveTheme: (theme: ThemeSettings) => Promise<void>;
};

export const AuthContext = createContext<AuthState | null>(null);

