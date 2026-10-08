import { useEffect, useState, useSyncExternalStore } from "react";
import Box from "@mui/material/Box";
import CircularProgress from "@mui/material/CircularProgress";
import { pendingRequests, subscribeToPendingRequests } from "../lib/api";
import ProgressBar from "./ProgressBar";

// A thin bar across the top of the window while the server is being asked
// for anything, on every page: the page's own placeholders say what is
// loading, and this says that something still is.
export default function LoadingBar() {
  const busy = useSyncExternalStore(subscribeToPendingRequests, pendingRequests) > 0;
  const [shown, setShown] = useState(false);

  useEffect(() => {
    // Not for a request answered at once: the bar would only flicker.
    const timer = window.setTimeout(() => setShown(busy), busy ? 200 : 0);
    return () => window.clearTimeout(timer);
  }, [busy]);

  if (!shown) return null;
  return (
    <ProgressBar label="Loading" height={3} sx={{ position: "fixed", top: 0, left: 0, right: 0, borderRadius: 0, zIndex: 1400 }} />
  );
}

// The whole window while it is not yet known who is signed in, when there is no page to show a placeholder in.
export function PageLoader() {
  return (
    <Box role="status" aria-label="Loading" sx={{ display: "grid", placeItems: "center", minHeight: "100vh" }}>
      <CircularProgress color="info" />
    </Box>
  );
}
