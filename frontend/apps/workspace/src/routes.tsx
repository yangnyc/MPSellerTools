import Icon from "@mui/material/Icon";
import DashboardPage from "./pages/DashboardPage";
import ProductsPage from "./pages/ProductsPage";
import OrdersPage from "./pages/OrdersPage";
import TasksPage from "./pages/TasksPage";
import UsersPage from "./pages/UsersPage";
import SettingsPage from "./pages/SettingsPage";
import AuditPage from "./pages/AuditPage";
import ProfilePage from "./pages/ProfilePage";

// Sidenav + router entries shared by TenantAdmin and Employee (brief §8).
// Routes without a `roles` restriction are visible to both; TenantAdmin-only
// entries (Users, Settings, Audit) are filtered out of the Employee's sidenav
// and, independently, rejected by the backend even if a request reaches the
// API directly (brief §5/§8's "direct requests to restricted APIs must also
// be denied").
export type AppRoute = {
  type: "collapse" | "title" | "divider";
  name?: string;
  key: string;
  icon?: React.ReactNode;
  route?: string;
  component?: React.ReactNode;
  roles?: string[];
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
  {
    type: "collapse",
    name: "Users",
    key: "users",
    icon: <Icon fontSize="small">group</Icon>,
    route: "/users",
    component: <UsersPage />,
    roles: ["TenantAdmin"],
  },
  {
    type: "collapse",
    name: "Products",
    key: "products",
    icon: <Icon fontSize="small">inventory_2</Icon>,
    route: "/products",
    component: <ProductsPage />,
  },
  {
    type: "collapse",
    name: "Orders",
    key: "orders",
    icon: <Icon fontSize="small">receipt_long</Icon>,
    route: "/orders",
    component: <OrdersPage />,
  },
  {
    type: "collapse",
    name: "Tasks",
    key: "tasks",
    icon: <Icon fontSize="small">checklist</Icon>,
    route: "/tasks",
    component: <TasksPage />,
  },
  {
    type: "collapse",
    name: "Settings",
    key: "settings",
    icon: <Icon fontSize="small">settings</Icon>,
    route: "/settings",
    component: <SettingsPage />,
    roles: ["TenantAdmin"],
  },
  {
    type: "collapse",
    name: "Audit",
    key: "audit",
    icon: <Icon fontSize="small">fact_check</Icon>,
    route: "/audit",
    component: <AuditPage />,
    roles: ["TenantAdmin"],
  },
  {
    type: "collapse",
    name: "Profile",
    key: "profile",
    icon: <Icon fontSize="small">person</Icon>,
    route: "/profile",
    component: <ProfilePage />,
  },
];

export default routes;
