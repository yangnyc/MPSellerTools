import { useEffect, useLayoutEffect, useMemo, useRef, type RefObject } from "react";
import { useMaterialUIController, applyThemeSettings, defaultThemeSettings, themeOptions } from "context";
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

const lookKey = (themeName: string, darkMode: boolean) => `${themeName}:${darkMode}`;

// What is known of one signed-in user's saved looks.
type Synced = {
  userId: string;
  // The settings the profile holds as the look in use.
  saved: string;
  // Each look used before (a named theme in light or in dark mode), and the
  // mode each theme was last used in.
  looks: Map<string, string>;
  lastDark: Map<string, boolean>;
  // False until the looks saved on the profile have been read.
  ready: boolean;
  // Set by a change of look made before then, to finish it once they are in.
  unfinished: "theme" | "mode" | null;
  // Saves go out one after another, and only once the saved looks are in.
  queue: Promise<void>;
};

type Refs = {
  synced: RefObject<Synced | null>;
  shown: RefObject<string | null>;
  latest: RefObject<{ saveTheme: (theme: ThemeSettings) => Promise<void> }>;
};

// Writes the settings to the profile as the look in use, after any save
// already under way. With `ifStillShown`, only if they are still on screen
// by then.
function save({ synced, shown, latest }: Refs, target: Synced, settings: string, ifStillShown = false) {
  target.queue = target.queue.then(async () => {
    if (synced.current !== target || target.saved === settings || (ifStillShown && shown.current !== settings)) {
      return;
    }
    target.saved = settings;
    await latest.current.saveTheme(parse(settings)).catch(() => {
      // Forget the failed save so the next change retries it.
      if (target.saved === settings) {
        target.saved = "";
      }
    });
  });
}

// The signed-in user's profile in the database is the only place theme
// choices (named theme, light/dark, sidenav colors, ...) live: this loads them from the
// profile on every sign-in and page load, saves any later change back to it,
// and returns to the defaults on sign-out. Each look (a named theme in light
// or in dark mode) keeps its own colours there. They are all read at sign-in,
// so that changing the theme or the mode shows the look being entered at
// once, with nothing else on screen in between; the look being left is
// written behind it.
// Renders nothing.
export default function ThemeSync() {
  const { user, status, saveTheme, loadTheme } = useAuth();
  const [controller, dispatch] = useMaterialUIController();
  const synced = useRef<Synced | null>(null);
  // The settings last on screen for this user, to notice a change of look.
  // Null while a profile that was just loaded is still being applied.
  const shown = useRef<string | null>(null);
  const latest = useRef({ user, saveTheme, loadTheme });
  // A layout effect like the one below, and ahead of it, so that one reads the user just signed in.
  useLayoutEffect(() => {
    latest.current = { user, saveTheme, loadTheme };
  });
  const refs = useMemo<Refs>(() => ({ synced, shown, latest }), []);

  const current = serialize(controller);
  const userId = status === "authenticated" ? (user?.id ?? null) : null;

  // A layout effect, so a look applied here replaces the preset before the
  // browser has painted it.
  useLayoutEffect(() => {
    if (!userId) {
      if (synced.current) {
        synced.current = null;
        shown.current = null;
        applyThemeSettings(dispatch, defaultThemeSettings);
      }
      return;
    }

    // Shows the saved look for the theme or mode just entered, if it was used before.
    const enter = (target: Synced, on: string, change: "theme" | "mode") => {
      const now = parse(on);
      // A new theme comes back in the mode it was last used in.
      const dark = change === "theme" ? (target.lastDark.get(now.themeName) ?? now.darkMode) : now.darkMode;
      const stored = target.looks.get(lookKey(now.themeName, dark));
      // A look the user has not used before keeps what is on screen: the
      // theme's preset, or the colours carried over from the other mode.
      if (!stored) {
        return;
      }
      const next = change === "theme" ? stored : serialize({ ...parse(stored), fixedNavbar: now.fixedNavbar });
      if (next !== on) {
        // Recorded as shown first, so applying it is not taken for another change.
        shown.current = next;
        applyThemeSettings(dispatch, parse(next));
      }
    };

    if (synced.current?.userId !== userId) {
      // A profile with nothing saved yet gets the defaults.
      const profileTheme = latest.current.user?.theme ?? defaultThemeSettings;
      const target: Synced = {
        userId,
        saved: serialize(profileTheme),
        looks: new Map(),
        lastDark: new Map(),
        ready: false,
        unfinished: null,
        queue: Promise.resolve(),
      };
      synced.current = target;
      if (target.saved !== current) {
        shown.current = null;
        applyThemeSettings(dispatch, profileTheme);
      } else {
        shown.current = current;
      }

      // What this session has already put in `looks` is newer than the profile's.
      const read = async (themeName: string, darkMode?: boolean) => {
        const stored = await latest.current.loadTheme(themeName, darkMode).catch(() => null);
        if (!stored || typeof stored.sidenavColor !== "string") {
          return;
        }
        const look = serialize({ ...stored, themeName });
        if (darkMode === undefined) {
          if (!target.lastDark.has(themeName)) {
            target.lastDark.set(themeName, stored.darkMode);
          }
        } else if (!target.looks.has(lookKey(themeName, darkMode))) {
          target.looks.set(lookKey(themeName, darkMode), look);
        }
      };
      const themeNames = themeOptions.map((option: { id: string }) => option.id);
      target.queue = Promise.all(
        themeNames.flatMap((themeName: string) => [read(themeName), read(themeName, false), read(themeName, true)])
      ).then(() => {
        target.ready = true;
        if (synced.current === target && target.unfinished && shown.current) {
          enter(target, shown.current, target.unfinished);
        }
        target.unfinished = null;
      });
      return;
    }

    const target = synced.current;
    const previous = shown.current;
    shown.current = current;
    if (!previous) {
      return;
    }

    const now = parse(current);
    const before = parse(previous);
    const change = before.themeName !== now.themeName ? "theme" : before.darkMode !== now.darkMode ? "mode" : null;
    if (!change) {
      return;
    }

    // The look being left is kept as it was, and written to the profile.
    target.looks.set(lookKey(before.themeName, before.darkMode), previous);
    target.lastDark.set(before.themeName, before.darkMode);
    save(refs, target, previous);

    if (target.ready) {
      enter(target, current, change);
    } else {
      target.unfinished = change;
    }
  }, [userId, current, dispatch, refs]);

  useEffect(() => {
    const target = synced.current;
    if (!userId || !target || shown.current !== current || target.saved === current) {
      return undefined;
    }

    // Debounced so clicking through several swatches sends one request.
    const timer = window.setTimeout(() => save(refs, target, current, true), 400);

    return () => window.clearTimeout(timer);
  }, [userId, current, refs]);

  return null;
}
