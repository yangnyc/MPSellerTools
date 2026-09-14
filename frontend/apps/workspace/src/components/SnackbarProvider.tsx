import { createContext, useCallback, useState, type ReactNode } from "react";
import MDSnackbar from "components/MDSnackbar";

type Severity = "success" | "error" | "warning" | "info";

export type SnackbarState = {
  notify: (message: string, severity?: Severity) => void;
};

export const SnackbarContext = createContext<SnackbarState | null>(null);

export function SnackbarProvider({ children }: { children: ReactNode }) {
  const [open, setOpen] = useState(false);
  const [message, setMessage] = useState("");
  const [severity, setSeverity] = useState<Severity>("info");

  const notify = useCallback((msg: string, sev: Severity = "info") => {
    setMessage(msg);
    setSeverity(sev);
    setOpen(true);
  }, []);

  return (
    <SnackbarContext.Provider value={{ notify }}>
      {children}
      <MDSnackbar
        color={severity}
        icon={severity === "error" ? "warning" : "notifications"}
        title="MPSellerTools"
        dateTime=""
        content={message}
        open={open}
        close={() => setOpen(false)}
      />
    </SnackbarContext.Provider>
  );
}
