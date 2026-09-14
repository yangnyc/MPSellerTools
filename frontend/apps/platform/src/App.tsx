import { useEffect, type ReactNode } from "react";
import { Routes, Route, Navigate, useLocation } from "react-router-dom";
import { ThemeProvider } from "@mui/material/styles";
import CssBaseline from "@mui/material/CssBaseline";
import Icon from "@mui/material/Icon";
import MDBox from "components/MDBox";
import Sidenav from "examples/Sidenav";
import Configurator from "examples/Configurator";
import theme from "assets/theme";
import themeDark from "assets/theme-dark";
import { useMaterialUIController, setOpenConfigurator } from "context";
import routes, { type AppRoute } from "./routes";
import LoginPage from "./pages/LoginPage";
import { useAuth } from "./auth/useAuth";

function renderProtectedRoutes(allRoutes: AppRoute[]) {
  return allRoutes
    .filter((route) => route.type === "collapse" && route.route)
    .map((route) => (
      <Route
        path={route.route}
        element={<RequireAuth>{route.component}</RequireAuth>}
        key={route.key}
      />
    ));
}

function RequireAuth({ children }: { children: ReactNode }) {
  const { status } = useAuth();

  if (status === "loading") {
    return null;
  }
  if (status === "anonymous") {
    return <Navigate to="/login" replace />;
  }
  return <>{children}</>;
}

export default function App() {
  const [controller, dispatch] = useMaterialUIController();
  const { layout, openConfigurator, sidenavColor, darkMode } = controller;
  const { pathname } = useLocation();
  const { status } = useAuth();

  useEffect(() => {
    document.documentElement.scrollTop = 0;
  }, [pathname]);

  const handleConfiguratorOpen = () => setOpenConfigurator(dispatch, !openConfigurator);

  const configsButton = (
    <MDBox
      display="flex"
      justifyContent="center"
      alignItems="center"
      width="3.25rem"
      height="3.25rem"
      bgColor="white"
      shadow="sm"
      borderRadius="50%"
      position="fixed"
      right="2rem"
      bottom="2rem"
      zIndex={99}
      color="dark"
      sx={{ cursor: "pointer" }}
      onClick={handleConfiguratorOpen}
    >
      <Icon fontSize="small" color="inherit">
        settings
      </Icon>
    </MDBox>
  );

  const showChrome = layout === "dashboard" && pathname !== "/login" && status === "authenticated";

  return (
    <ThemeProvider theme={darkMode ? themeDark : theme}>
      <CssBaseline />
      {showChrome && (
        <>
          <Sidenav color={sidenavColor} brandName="MPSellerTools" routes={routes} />
          <Configurator />
          {configsButton}
        </>
      )}
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        {renderProtectedRoutes(routes)}
        <Route path="*" element={<Navigate to="/dashboard" replace />} />
      </Routes>
    </ThemeProvider>
  );
}
