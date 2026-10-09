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
  // The sidebar menu groups kept pinned open, by group key (see routes.tsx).
  pinnedMenus?: string[];
  // Whether users are added by invitation. Off (or not said), they are added with a password instead.
  invitationsEnabled?: boolean;
};

export type AuthState = {
  user: CurrentUser | null;
  status: "loading" | "authenticated" | "anonymous";
  login: (email: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
  updateDisplayName: (displayName: string) => Promise<void>;
  saveTheme: (theme: ThemeSettings) => Promise<void>;
  // The settings last saved for a named theme, or null if it was never used
  // that way: in light or dark mode when `darkMode` is given, otherwise in
  // whichever mode the theme was last used in.
  loadTheme: (themeName: string, darkMode?: boolean) => Promise<ThemeSettings | null>;
  savePinnedMenus: (menus: string[]) => Promise<void>;
};

export const AuthContext = createContext<AuthState | null>(null);

