import { useEffect } from "react";
import { ThemeProvider } from "@mui/material/styles";
import CssBaseline from "@mui/material/CssBaseline";
import { useMaterialUIController } from "context";
import theme from "assets/theme";
import themeDark from "assets/theme-dark";
import { AuthProvider } from "./auth/AuthContext";
import { SnackbarProvider } from "./components/SnackbarProvider";
import App from "./App";

// MDSnackbar renders as a sibling of {children} in SnackbarProvider (not a
// descendant of App), so ThemeProvider must wrap SnackbarProvider itself —
// otherwise the snackbar falls back to MUI's default theme, which lacks the
// custom theme.functions/palette.gradients these components read from.
export default function ThemedApp() {
  const [{ darkMode }] = useMaterialUIController();

  // Roboto loads via a render-blocking-free <link> with `display=swap`, so
  // the first paint can use a fallback font. MUI measures each outlined
  // field's floating-label width once to size the notch cut into the
  // border; if that measurement happens before Roboto swaps in, the notch
  // stays sized for the fallback font's (narrower) metrics and the border
  // visibly cuts through the label once Roboto renders. Firing a resize
  // event once the webfont is actually ready makes MUI re-measure and
  // re-notch every mounted outlined field.
  useEffect(() => {
    document.fonts?.ready.then(() => {
      window.dispatchEvent(new Event("resize"));
    });
  }, []);

  return (
    <ThemeProvider theme={darkMode ? themeDark : theme}>
      <CssBaseline />
      <AuthProvider>
        <SnackbarProvider>
          <App />
        </SnackbarProvider>
      </AuthProvider>
    </ThemeProvider>
  );
}

