import { useEffect, useRef } from "react";
import { useMaterialUIController, applyThemeSettings, defaultThemeSettings } from "context";
import { useAuth } from "./useAuth";
import type { ThemeSettings } from "./authState";

// Fixed key order, so two settings objects can be compared as JSON strings.
// A profile saved before themes had names counts as the default theme.
function serialize(theme: ThemeSettings): string {
  const { darkMode, whiteSidenav, sidenavTint, sidenavColor, fixedNavbar } = theme;
  const themeName = theme.themeName ?? defaultThemeSettings.themeName;
  return JSON.stringify({ themeName, darkMode, whiteSidenav, sidenavTint, sidenavColor, fixedNavbar });
}

// The signed-in user's profile in the database is the only place theme
// choices (named theme, light/dark, sidenav colors, ...) live: this loads them from the
// profile on every sign-in and page load, saves any later change back to it,
// and returns to the defaults on sign-out. Renders nothing.
export default function ThemeSync() {
  const { user, status, saveTheme } = useAuth();
  const [controller, dispatch] = useMaterialUIController();
  const synced = useRef<{ userId: string; saved: string } | null>(null);
  const latest = useRef({ user, saveTheme });
  useEffect(() => {
    latest.current = { user, saveTheme };
  });

  const current = serialize(controller);
  const userId = status === "authenticated" ? (user?.id ?? null) : null;

  useEffect(() => {
    if (!userId) {
      if (synced.current) {
        synced.current = null;
        applyThemeSettings(dispatch, defaultThemeSettings);
      }
      return undefined;
    }

    if (synced.current?.userId !== userId) {
      // A profile with nothing saved yet gets the defaults.
      const profileTheme = latest.current.user?.theme ?? defaultThemeSettings;
      synced.current = { userId, saved: serialize(profileTheme) };
      if (synced.current.saved !== current) {
        applyThemeSettings(dispatch, profileTheme);
      }
      return undefined;
    }

    if (synced.current.saved === current) {
      return undefined;
    }

    // Debounced so clicking through several swatches sends one request.
    const timer = window.setTimeout(() => {
      const target = synced.current;
      if (!target) {
        return;
      }
      target.saved = current;
      latest.current.saveTheme(JSON.parse(current) as ThemeSettings).catch(() => {
        // Forget the failed save so the next change retries it.
        if (target.saved === current) {
          target.saved = "";
        }
      });
    }, 400);

    return () => window.clearTimeout(timer);
  }, [userId, current, dispatch]);

  return null;
}