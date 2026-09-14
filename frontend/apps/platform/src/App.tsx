import { useEffect } from "react";
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

function renderRoutes(allRoutes: AppRoute[]) {
  return allRoutes
    .filter((route) => route.type === "collapse" && route.route)
    .map((route) => <Route path={route.route} element={route.component} key={route.key} />);
}

export default function App() {
  const [controller, dispatch] = useMaterialUIController();
  const { layout, openConfigurator, sidenavColor, darkMode } = controller;
  const { pathname } = useLocation();

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

  return (
    <ThemeProvider theme={darkMode ? themeDark : theme}>
      <CssBaseline />
      {layout === "dashboard" && pathname !== "/login" && (
        <>
          <Sidenav color={sidenavColor} brandName="MPSellerTools" routes={routes} />
          <Configurator />
          {configsButton}
        </>
      )}
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        {renderRoutes(routes)}
        <Route path="*" element={<Navigate to="/dashboard" replace />} />
      </Routes>
    </ThemeProvider>
  );
}
