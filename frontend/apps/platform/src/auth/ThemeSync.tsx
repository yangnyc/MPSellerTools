import { useEffect, useRef, useState } from "react";
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

const parse = (serialized: string) => JSON.parse(serialized) as ThemeSettings & { themeName: string };

// The signed-in user's profile in the database is the only place theme
// choices (named theme, light/dark, sidenav colors, ...) live: this loads them from the
// profile on every sign-in and page load, saves any later change back to it,
// and returns to the defaults on sign-out. Each look (a named theme in light
// or in dark mode) keeps its own colours there: changing the theme or the
// mode first writes the look being left, then reads the one being entered.
// Renders nothing.
export default function ThemeSync() {
  const { user, status, saveTheme, loadTheme } = useAuth();
  const [controller, dispatch] = useMaterialUIController();
  const synced = useRef<{ userId: string; saved: string } | null>(null);
  // The settings last on screen for this user, to notice a change of look.
  // Null while a profile that was just loaded is still being applied.
  const shown = useRef<string | null>(null);
  // Changes of look run one after another; only the newest one's result is used.
  const switches = useRef({ count: 0, pending: false, queue: Promise.resolve() });
  const [settled, setSettled] = useState(0);
  const latest = useRef({ user, saveTheme, loadTheme });
  useEffect(() => {
    latest.current = { user, saveTheme, loadTheme };
  });

  const current = serialize(controller);
  const userId = status === "authenticated" ? (user?.id ?? null) : null;

  useEffect(() => {
    if (!userId) {
      if (synced.current) {
        synced.current = null;
        shown.current = null;
        switches.current.count += 1;
        switches.current.pending = false;
        applyThemeSettings(dispatch, defaultThemeSettings);
      }
      return undefined;
    }

    if (synced.current?.userId !== userId) {
      // A profile with nothing saved yet gets the defaults.
      const profileTheme = latest.current.user?.theme ?? defaultThemeSettings;
      synced.current = { userId, saved: serialize(profileTheme) };
      switches.current.count += 1;
      switches.current.pending = false;
      if (synced.current.saved !== current) {
        shown.current = null;
        applyThemeSettings(dispatch, profileTheme);
      } else {
        shown.current = current;
      }
      return undefined;
    }

    const target = synced.current;
    const previous = shown.current;
    shown.current = current;

    const now = parse(current);
    const before = previous ? parse(previous) : null;
    const themeChanged = before !== null && before.themeName !== now.themeName;
    const modeChanged = before !== null && before.darkMode !== now.darkMode;

    if (previous && (themeChanged || modeChanged)) {
      // Mid-switch, what was on screen is a preset or the other mode's
      // colours rather than anything the user chose, so it is not worth keeping.
      const writeOld = !switches.current.pending;
      const run = ++switches.current.count;
      switches.current.pending = true;
      switches.current.queue = switches.current.queue.then(async () => {
        if (writeOld && target.saved !== previous) {
          target.saved = previous;
          await latest.current.saveTheme(parse(previous)).catch(() => {
            if (target.saved === previous) {
              target.saved = "";
            }
          });
        }
        // A new theme comes back in the mode it was last used in; a change of
        // mode alone asks for this theme's colours in that mode.
        const stored = await latest.current
          .loadTheme(now.themeName, themeChanged ? undefined : now.darkMode)
          .catch(() => null);
        if (run !== switches.current.count || synced.current !== target) {
          return;
        }
        switches.current.pending = false;
        // A look the user has not used before keeps what is on screen: the
        // theme's preset, or the colours carried over from the other mode.
        if (stored) {
          const next = themeChanged
            ? { ...stored, themeName: now.themeName }
            : { ...stored, themeName: now.themeName, darkMode: now.darkMode, fixedNavbar: now.fixedNavbar };
          // Recorded as shown first, so applying it is not taken for another switch.
          shown.current = serialize(next);
          applyThemeSettings(dispatch, next);
        }
        // Re-runs this effect, which saves the new look as the one in use.
        setSettled(run);
      });
      return undefined;
    }

    if (switches.current.pending || target.saved === current) {
      return undefined;
    }

    // Debounced so clicking through several swatches sends one request.
    const timer = window.setTimeout(() => {
      if (synced.current !== target) {
        return;
      }
      target.saved = current;
      latest.current.saveTheme(parse(current)).catch(() => {
        // Forget the failed save so the next change retries it.
        if (target.saved === current) {
          target.saved = "";
        }
      });
    }, 400);

    return () => window.clearTimeout(timer);
  }, [userId, current, dispatch, settled]);

  return null;
}
