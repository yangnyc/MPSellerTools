import { createContext } from "react";

export type Severity = "success" | "error" | "warning" | "info";

export type SnackbarState = {
  notify: (message: string, severity?: Severity) => void;
};

export const SnackbarContext = createContext<SnackbarState | null>(null);

