import { useEffect, useState } from "react";
import Card from "@mui/material/Card";
import Chip from "@mui/material/Chip";
import Dialog from "@mui/material/Dialog";
import DialogTitle from "@mui/material/DialogTitle";
import DialogContent from "@mui/material/DialogContent";
import DialogActions from "@mui/material/DialogActions";
import Icon from "@mui/material/Icon";
import IconButton from "@mui/material/IconButton";
import MDBox from "components/MDBox";
import MDTypography from "components/MDTypography";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import DataTable from "examples/Tables/DataTable";
import DashboardLayout from "examples/LayoutContainers/DashboardLayout";
import DashboardNavbar from "examples/Navbars/DashboardNavbar";
import Footer from "examples/Footer";
import { useAuth } from "../auth/useAuth";
import { useSnackbar } from "../components/useSnackbar";
import { ApiError } from "../lib/api";
import { OrdersApi, ProductsApi, UsersApi } from "../api/resources";
import { ORDER_STATUS_LABELS, type Order, type OrderStatus, type Product, type UserSummary } from "../api/types";

const NEXT_STATUSES: Record<OrderStatus, OrderStatus[]> = {
  0: [1, 3],
  1: [2, 3],
  2: [],
  3: [],
};

type ItemRow = { productId: string; quantity: string };

export default function OrdersPage() {
  const { user, logout } = useAuth();
  const { notify } = useSnackbar();
  const isTenantAdmin = user?.roles.includes("TenantAdmin") ?? false;

  const [orders, setOrders] = useState<Order[] | null>(null);
  const [products, setProducts] = useState<Product[]>([]);
  const [users, setUsers] = useState<UserSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [formOpen, setFormOpen] = useState(false);
  const [items, setItems] = useState<ItemRow[]>([{ productId: "", quantity: "1" }]);
  const [assignedUserId, setAssignedUserId] = useState<string>("");
  const [formError, setFormError] = useState<string | null>(null);

  const load = () => {
    setLoading(true);
    setError(null);
    const requests: Promise<unknown>[] = [OrdersApi.list().then(setOrders), ProductsApi.list().then(setProducts)];
    if (isTenantAdmin) {
      requests.push(UsersApi.list().then(setUsers));
    }
    Promise.all(requests)
      .catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load orders."))
      .finally(() => setLoading(false));
  };

  useEffect(load, [isTenantAdmin]);

  const openCreate = () => {
    setItems([{ productId: "", quantity: "1" }]);
    setAssignedUserId("");
    setFormError(null);
    setFormOpen(true);
  };

  const submitForm = async () => {
    const parsedItems = items
      .filter((i) => i.productId)
      .map((i) => ({ productId: i.productId, quantity: Number(i.quantity) }));

    if (parsedItems.length === 0) {
      setFormError("Add at least one item.");
      return;
    }
    if (parsedItems.some((i) => !Number.isInteger(i.quantity) || i.quantity <= 0)) {
      setFormError("Quantities must be positive whole numbers.");
      return;
    }

    try {
      await OrdersApi.create({ items: parsedItems, assignedUserId: assignedUserId || null });
      notify("Order created.", "success");
      setFormOpen(false);
      load();
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        await logout();
        return;
      }
      setFormError(err instanceof ApiError ? err.message : "Save failed.");
    }
  };

  const changeStatus = async (order: Order, status: OrderStatus) => {
    try {
      await OrdersApi.changeStatus(order.id, status, order.rowVersion);
      notify(`Order ${order.orderNumber} moved to ${ORDER_STATUS_LABELS[status]}.`, "success");
      load();
    } catch (err) {
      notify(err instanceof ApiError ? err.message : "Status change failed.", "error");
    }
  };

  const columns = [
    { Header: "Order #", accessor: "orderNumber" },
    { Header: "Status", accessor: "status" },
    { Header: "Assigned to", accessor: "assignedTo" },
    { Header: "Total", accessor: "total", align: "right" as const },
    ...(isTenantAdmin ? [{ Header: "Actions", accessor: "actions", align: "right" as const }] : []),
  ];

  const userNameById = (id: string | null) => users.find((u) => u.id === id)?.displayName ?? (id ? "Unknown" : "Unassigned");

  const rows =
    orders?.map((o) => ({
      orderNumber: o.orderNumber,
      status: <Chip size="small" label={ORDER_STATUS_LABELS[o.status]} />,
      assignedTo: isTenantAdmin ? userNameById(o.assignedUserId) : ORDER_STATUS_LABELS[o.status],
      total: `$${o.total.toFixed(2)}`,
      actions: isTenantAdmin ? (
        <MDBox display="flex" justifyContent="flex-end" gap={1}>
          {NEXT_STATUSES[o.status].map((next) => (
            <MDButton key={next} size="small" variant="outlined" color="info" onClick={() => changeStatus(o, next)}>
              {ORDER_STATUS_LABELS[next]}
            </MDButton>
          ))}
        </MDBox>
      ) : null,
    })) ?? [];

  return (
    <DashboardLayout>
      <DashboardNavbar onLogout={logout} />
      <MDBox py={3}>
        <Card>
          <MDBox display="flex" justifyContent="space-between" alignItems="center" p={3}>
            <MDTypography variant="h5">Orders</MDTypography>
            {isTenantAdmin && (
              <MDButton variant="gradient" color="info" onClick={openCreate}>
                Create order
              </MDButton>
            )}
          </MDBox>
          {loading && (
            <MDBox p={3}>
              <MDTypography variant="body2">Loading orders…</MDTypography>
            </MDBox>
          )}
          {error && (
            <MDBox p={3}>
              <MDTypography variant="body2" color="error">
                {error}
              </MDTypography>
            </MDBox>
          )}
          {!loading && !error && orders?.length === 0 && (
            <MDBox p={3}>
              <MDTypography variant="body2" color="text">
                {isTenantAdmin ? "No orders yet." : "No orders are currently assigned to you."}
              </MDTypography>
            </MDBox>
          )}
          {!loading && !error && (orders?.length ?? 0) > 0 && <DataTable table={{ columns, rows }} canSearch />}
        </Card>
      </MDBox>
      <Footer />

      <Dialog open={formOpen} onClose={() => setFormOpen(false)} fullWidth maxWidth="sm">
        <DialogTitle>Create order</DialogTitle>
        <DialogContent>
          {formError && (
            <MDBox mb={2}>
              <MDTypography variant="caption" color="error">
                {formError}
              </MDTypography>
            </MDBox>
          )}
          {items.map((item, index) => (
            <MDBox key={index} display="flex" gap={1} alignItems="center" mb={1.5} mt={index === 0 ? 1 : 0}>
              <MDInput
                select
                label="Product"
                fullWidth
                SelectProps={{ native: true }}
                value={item.productId}
                onChange={(e: React.ChangeEvent<HTMLInputElement>) => {
                  const next = [...items];
                  next[index] = { ...next[index], productId: e.target.value };
                  setItems(next);
                }}
              >
                <option value="" />
                {products.map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.sku} — {p.name} (${p.price.toFixed(2)})
                  </option>
                ))}
              </MDInput>
              <MDInput
                label="Qty"
                type="number"
                sx={{ width: "6rem" }}
                value={item.quantity}
                onChange={(e: React.ChangeEvent<HTMLInputElement>) => {
                  const next = [...items];
                  next[index] = { ...next[index], quantity: e.target.value };
                  setItems(next);
                }}
              />
              <IconButton
                size="small"
                onClick={() => setItems(items.filter((_, i) => i !== index))}
                disabled={items.length === 1}
                aria-label="Remove item"
              >
                <Icon fontSize="small">delete</Icon>
              </IconButton>
            </MDBox>
          ))}
          <MDButton
            size="small"
            variant="text"
            color="info"
            onClick={() => setItems([...items, { productId: "", quantity: "1" }])}
          >
            + Add item
          </MDButton>
          <MDBox mt={2}>
            <MDInput
              select
              label="Assign to"
              fullWidth
              SelectProps={{ native: true }}
              value={assignedUserId}
              onChange={(e: React.ChangeEvent<HTMLInputElement>) => setAssignedUserId(e.target.value)}
            >
              <option value="">Unassigned</option>
              {users
                .filter((u) => !u.isBlocked)
                .map((u) => (
                  <option key={u.id} value={u.id}>
                    {u.displayName} ({u.email})
                  </option>
                ))}
            </MDInput>
          </MDBox>
        </DialogContent>
        <DialogActions>
          <MDButton variant="text" color="secondary" onClick={() => setFormOpen(false)}>
            Cancel
          </MDButton>
          <MDButton variant="gradient" color="info" onClick={submitForm}>
            Create
          </MDButton>
        </DialogActions>
      </Dialog>
    </DashboardLayout>
  );
}
