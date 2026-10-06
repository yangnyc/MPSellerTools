import Icon from "@mui/material/Icon";
import DashboardPage from "./pages/DashboardPage";
import ProductsPage from "./pages/ProductsPage";
import ListingsPage from "./pages/ListingsPage";
import OrdersPage from "./pages/OrdersPage";
import TasksPage from "./pages/TasksPage";
import UsersPage from "./pages/UsersPage";
import SettingsPage from "./pages/SettingsPage";
import EbayPage from "./pages/EbayPage";
import AuditPage from "./pages/AuditPage";
import ProfilePage from "./pages/ProfilePage";
import UserBulkPage from "./pages/advanced/UserBulkPage";
import UserExportPage from "./pages/advanced/UserExportPage";
import UserControlsPage from "./pages/advanced/UserControlsPage";
import UserInvitationsPage from "./pages/advanced/UserInvitationsPage";
import UserRolesPage from "./pages/advanced/UserRolesPage";
import UserActivityPage from "./pages/advanced/UserActivityPage";

// Sidenav + router entries shared by TenantAdmin and Employee (brief §8).
// Routes without a `roles` restriction are visible to both; TenantAdmin-only
// entries (Users, eBay, Settings, Audit) are filtered out of the Employee's sidenav
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
  // Sub-items: makes this an expandable sidenav group with no page of its own.
  collapse?: AppRoute[];
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
    name: "Products",
    key: "products",
    icon: <Icon fontSize="small">inventory_2</Icon>,
    route: "/products",
    component: <ProductsPage />,
  },
  {
    type: "collapse",
    name: "Listings",
    key: "listings",
    icon: <Icon fontSize="small">sell</Icon>,
    route: "/listings",
    component: <ListingsPage />,
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
    name: "eBay",
    key: "ebay",
    icon: <Icon fontSize="small">storefront</Icon>,
    route: "/ebay",
    component: <EbayPage />,
    roles: ["TenantAdmin"],
  },
  // Between the lines: following up on the work, apart from the selling above.
  { type: "divider", key: "divider-follow-up" },
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
    name: "Audit",
    key: "audit",
    icon: <Icon fontSize="small">fact_check</Icon>,
    route: "/audit",
    component: <AuditPage />,
    roles: ["TenantAdmin"],
  },
  // Below the line: the company's own setup and people, apart from the daily work above it.
  { type: "divider", key: "divider-admin" },
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
    name: "Profile",
    key: "profile",
    icon: <Icon fontSize="small">person</Icon>,
    route: "/profile",
    component: <ProfilePage />,
  },
  {
    type: "collapse",
    name: "Users",
    key: "users",
    icon: <Icon fontSize="small">group</Icon>,
    roles: ["TenantAdmin"],
    collapse: [
      {
        type: "collapse",
        name: "All users",
        key: "users-list",
        icon: <Icon fontSize="small">view_list</Icon>,
        route: "/users",
        component: <UsersPage />,
        roles: ["TenantAdmin"],
      },
      {
        type: "collapse",
        name: "Bulk actions",
        key: "users-bulk",
        icon: <Icon fontSize="small">checklist_rtl</Icon>,
        route: "/users/bulk",
        component: <UserBulkPage />,
        roles: ["TenantAdmin"],
      },
      {
        type: "collapse",
        name: "Filters & export",
        key: "users-export",
        icon: <Icon fontSize="small">file_download</Icon>,
        route: "/users/export",
        component: <UserExportPage />,
        roles: ["TenantAdmin"],
      },
      {
        type: "collapse",
        name: "Account controls",
        key: "users-controls",
        icon: <Icon fontSize="small">manage_accounts</Icon>,
        route: "/users/controls",
        component: <UserControlsPage />,
        roles: ["TenantAdmin"],
      },
      {
        type: "collapse",
        name: "Invitations",
        key: "users-invitations",
        icon: <Icon fontSize="small">mail</Icon>,
        route: "/users/invitations",
        component: <UserInvitationsPage />,
        roles: ["TenantAdmin"],
      },
      {
        type: "collapse",
        name: "Roles & access",
        key: "users-roles",
        icon: <Icon fontSize="small">admin_panel_settings</Icon>,
        route: "/users/roles",
        component: <UserRolesPage />,
        roles: ["TenantAdmin"],
      },
      {
        type: "collapse",
        name: "User activity",
        key: "users-activity",
        icon: <Icon fontSize="small">timeline</Icon>,
        route: "/users/activity",
        component: <UserActivityPage />,
        roles: ["TenantAdmin"],
      },
    ],
  },
];

export default routes;
