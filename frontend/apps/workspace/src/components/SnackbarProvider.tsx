import { useCallback, useEffect, useState, type ReactNode } from "react";
import MDSnackbar from "components/MDSnackbar";

import { useAuth } from "../auth/useAuth";
import { apiFetch } from "../lib/api";
import { JOB_FINISHED } from "./jobWatch";
import { SnackbarContext, type NotificationEntry, type Severity, type StickyAlert } from "./snackbarState";

// The notifications view keeps this many; older ones drop off the end.
const maxNotifications = 500;

type Kept = { items: NotificationEntry[]; unread: number };

// Kept in the browser for each user, so the list is still there after a
// reload or signing in again. Storage that is full, switched off or holding
// something else simply means starting empty.
const storageKey = (userId: string) => `mpst.notifications.${userId}`;

function load(userId: string | null): Kept {
  try {
    const kept = userId ? JSON.parse(window.localStorage.getItem(storageKey(userId)) ?? "null") : null;
    if (kept && Array.isArray(kept.items)) {
      const items = (kept.items as NotificationEntry[]).filter((item) => typeof item?.id === "number" && typeof item.message === "string");
      return { items, unread: Math.min(Number(kept.unread) || 0, items.length) };
    }
  } catch {
    // Unreadable: start empty.
  }
  return { items: [], unread: 0 };
}

export function SnackbarProvider({ children }: { children: ReactNode }) {
  const [open, setOpen] = useState(false);
  const [message, setMessage] = useState("");
  const [severity, setSeverity] = useState<Severity>("info");
  // Counts notifications, so each one gets its own five seconds even when it
  // replaces one that is still showing.
  const [shownCount, setShownCount] = useState(0);
  // The list belongs to whoever is signed in: signing out, or in as someone
  // else, puts that user's own list in its place.
  const userId = useAuth().user?.id ?? null;
  const [ownerId, setOwnerId] = useState(userId);
  const [notifications, setNotifications] = useState<NotificationEntry[]>(() => load(userId).items);
  const [unreadCount, setUnreadCount] = useState(() => load(userId).unread);
  if (ownerId !== userId) {
    const kept = load(userId);
    setOwnerId(userId);
    setNotifications(kept.items);
    setUnreadCount(kept.unread);
  }

  useEffect(() => {
    if (!ownerId) return;
    try {
      window.localStorage.setItem(storageKey(ownerId), JSON.stringify({ items: notifications, unread: unreadCount }));
    } catch {
      // Not kept this time; the list still works for this visit.
    }
  }, [ownerId, notifications, unreadCount]);

  // The standing problems are the server's to say: asked for on signing in, every minute after, when a
  // background job finishes, and whenever a page says it has just put one right.
  // Kept with whose they are, so one user's are never shown to the next.
  const [found, setFound] = useState<{ owner: string | null; items: StickyAlert[] }>({ owner: null, items: [] });
  const alerts = found.owner === ownerId ? found.items : [];
  const refreshAlerts = useCallback(() => {
    if (!ownerId) return;
    apiFetch<StickyAlert[]>("/api/alerts")
      .then((items) => setFound({ owner: ownerId, items: Array.isArray(items) ? items : [] }))
      // Not known this time; what was last known stays shown.
      .catch(() => undefined);
  }, [ownerId]);
  useEffect(() => {
    if (!ownerId) return undefined;
    refreshAlerts();
    const timer = window.setInterval(refreshAlerts, 60000);
    window.addEventListener(JOB_FINISHED, refreshAlerts);
    return () => {
      window.clearInterval(timer);
      window.removeEventListener(JOB_FINISHED, refreshAlerts);
    };
  }, [ownerId, refreshAlerts]);

  const notify = useCallback((msg: string, sev: Severity = "info") => {
    const at = new Date().toISOString();
    setMessage(msg);
    setSeverity(sev);
    setShownCount((count) => count + 1);
    // Numbered on from the newest one kept, which is first.
    setNotifications((list) => [{ id: (list[0]?.id ?? 0) + 1, message: msg, severity: sev, at }, ...list].slice(0, maxNotifications));
    setUnreadCount((count) => Math.min(count + 1, maxNotifications));
    setOpen(true);
  }, []);

  const markNotificationsRead = useCallback(() => setUnreadCount(0), []);

  const clearNotifications = useCallback(() => {
    setNotifications([]);
    setUnreadCount(0);
  }, []);

  return (
    <SnackbarContext.Provider
      value={{ notify, notifications, unreadCount, markNotificationsRead, clearNotifications, alerts, refreshAlerts }}
    >
      {children}
      <MDSnackbar
        key={shownCount}
        color={severity}
        icon={severity === "error" ? "warning" : "notifications"}
        title="MP Seller Tools"
        dateTime=""
        content={message}
        open={open}
        close={() => setOpen(false)}
        // Disappears by itself after five seconds; a click elsewhere on the
        // page does not dismiss it early.
        autoHideDuration={5000}
        onClose={(_event: unknown, reason: string) => {
          if (reason !== "clickaway") {
            setOpen(false);
          }
        }}
      />
    </SnackbarContext.Provider>
  );
}
