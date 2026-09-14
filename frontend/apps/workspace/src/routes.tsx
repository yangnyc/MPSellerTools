import Icon from "@mui/material/Icon";
import DashboardPage from "./pages/DashboardPage";

// Sidenav + router entries shared by TenantAdmin and Employee (brief §8); the
// Employee variant filters this list at render time once roles exist
// (Increment 5). Additional entries (/users, /products, /orders, /tasks,
// /settings, /audit, /profile) are added in Increment 5.
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
