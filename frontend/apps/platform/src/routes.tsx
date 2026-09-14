import Icon from "@mui/material/Icon";
import DashboardPage from "./pages/DashboardPage";

// Sidenav + router entries for the PlatformAdmin console (brief §8).
// Additional entries (/tenants, /jobs, /audit, /profile) are added in
// Increment 5 alongside their real API-backed pages.
export type AppRoute = {
  type: "collapse" | "title" | "divider";
  name?: string;
  key: string;
  icon?: React.ReactNode;
  route?: string;
  component?: React.ReactNode;
};

const routes: AppRoute[] = [
  {
    type: "collapse",
    name: "Dashboard",
    key: "dashboard",
    icon: <Icon fontSize="small">dashboard</Icon>,
    route: "/dashboard",
    component: <DashboardPage />,
  },
];

export default routes;
