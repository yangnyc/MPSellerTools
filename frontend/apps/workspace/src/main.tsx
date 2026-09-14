import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import { MaterialUIControllerProvider } from "context";
import { AuthProvider } from "./auth/AuthContext";
import { SnackbarProvider } from "./components/SnackbarProvider";
import "./index.css";
import App from "./App";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <BrowserRouter>
      <MaterialUIControllerProvider>
        <AuthProvider>
          <SnackbarProvider>
            <App />
          </SnackbarProvider>
        </AuthProvider>
      </MaterialUIControllerProvider>
    </BrowserRouter>
  </StrictMode>
);
