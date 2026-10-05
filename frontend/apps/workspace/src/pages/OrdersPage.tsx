import { useCallback, useEffect, useMemo, useState } from "react";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import IconButton from "@mui/material/IconButton";
import Tooltip from "@mui/material/Tooltip";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import DataTable from "examples/Tables/DataTable";
import {
  DetailList,
  FilterTabs,
  Identity,
  InlineAlert,
  KitDialog,
  PageHeader,
  Section,
  StateBlock,
  StatusPill,
  formatDateTime,
  formatMoney,
  timeAgo,
  useKit,
} from "examples/Kit";
import PageShell from "../components/PageShell";
import { useAuth } from "../auth/useAuth";
import { useSnackbar } from "../components/useSnackbar";
import { ApiError } from "../lib/api";
import { OrdersApi, ProductsApi, UsersApi } from "../api/resources";
import { ORDER_STATUS_LABELS, type Order, type OrderStatus, type Product, type UserSummary } from "../api/types";
import { NEXT_STATUSES, ORDER_STATUS_TONE } from "../lib/status";

type ItemRow = { productId: string; quantity: string };
type StatusFilter = "all" | OrderStatus;

const ORDER_STATUSES: OrderStatus[] = [0, 1, 2, 3];

export default function OrdersPage() {
  const { user, logout } = useAuth();
  const { notify } = useSnackbar();
  const { c } = useKit();
  const isTenantAdmin = user?.roles.includes("TenantAdmin") ?? false;

  const [orders, setOrders] = useState<Order[] | null>(null);
  const [products, setProducts] = useState<Product[]>([]);
  const [users, setUsers] = useState<UserSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("all");
  const [detailId, setDetailId] = useState<string | null>(null);

  const [formOpen, setFormOpen] = useState(false);
  const [items, setItems] = useState<ItemRow[]>([{ productId: "", quantity: "1" }]);
  const [assignedUserId, setAssignedUserId] = useState<string>("");
  const [formError, setFormError] = useState<string | null>(null);

  const fetchData = useCallback(() => {
    const requests: Promise<unknown>[] = [OrdersApi.list().then(setOrders), ProductsApi.list().then(setProducts)];
    if (isTenantAdmin) {
      requests.push(UsersApi.list().then(setUsers));
    }
    Promise.all(requests)
      .catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load orders."))
      .finally(() => setLoading(false));
  }, [isTenantAdmin]);

  useEffect(fetchData, [fetchData]);

  const load = useCallback(() => {
    setLoading(true);
    setError(null);
    fetchData();
  }, [fetchData]);

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

  const changeStatus = useCallback(
    async (order: Order, status: OrderStatus) => {
      try {
        await OrdersApi.changeStatus(order.id, status, order.rowVersion);
        notify(`Order ${order.orderNumber} moved to ${ORDER_STATUS_LABELS[status]}.`, "success");
        load();
      } catch (err) {
        notify(err instanceof ApiError ? err.message : "Status change failed.", "error");
      }
    },
    [notify, load]
  );

  const assigneeName = useCallback(
    (id: string | null) => users.find((u) => u.id === id)?.displayName ?? (id ? "Unknown" : "Unassigned"),
    [users]
  );

  const statusActions = useCallback(
    (order: Order) =>
      NEXT_STATUSES[order.status].map((next) => (
        <MDButton
          key={next}
          size="small"
          variant="outlined"
          color={next === 3 ? "secondary" : "info"}
          onClick={() => changeStatus(order, next as OrderStatus)}
        >
          {next === 3 ? "Cancel order" : `Mark ${ORDER_STATUS_LABELS[next as OrderStatus].toLowerCase()}`}
        </MDButton>
      )),
    [changeStatus]
  );

  // Plain values in the rows (so the table's search and sorting work on them)
  // and the presentation in each column's Cell. Memoised because the table
  // resets its page and filter whenever it is handed new row objects.
  const table = useMemo(() => {
    type Row = { order: Order; orderNumber: string; statusLabel: string; assignedTo: string; total: number };
    type CellProps = { row: { original: Row } };

    const rows: Row[] = (orders ?? [])
      .filter((o) => statusFilter === "all" || o.status === statusFilter)
      .map((o) => ({
        order: o,
        orderNumber: o.orderNumber,
        statusLabel: ORDER_STATUS_LABELS[o.status],
        assignedTo: assigneeName(o.assignedUserId),
        total: o.total,
      }));

    const columns = [
      {
        Header: "Order",
        accessor: "orderNumber",
        Cell: ({ row }: CellProps) => {
          const o = row.original.order;
          return (
            <Box sx={{ lineHeight: 1.35 }}>
              <Box sx={{ fontWeight: 500, color: c.text }}>{o.orderNumber}</Box>
              <Box sx={{ fontSize: "0.75rem", color: c.muted }} title={formatDateTime(o.createdAtUtc)}>
                {o.items.length} {o.items.length === 1 ? "item" : "items"} · {timeAgo(o.createdAtUtc)}
              </Box>
            </Box>
          );
        },
      },
      {
        Header: "Status",
        accessor: "statusLabel",
        Cell: ({ row }: CellProps) => (
          <StatusPill tone={ORDER_STATUS_TONE[row.original.order.status]} label={row.original.statusLabel} />
        ),
      },
      ...(isTenantAdmin
        ? [
            {
              Header: "Assigned to",
              accessor: "assignedTo",
              Cell: ({ row }: CellProps) =>
                row.original.order.assignedUserId ? (
                  <Identity name={row.original.assignedTo} size={28} />
                ) : (
                  <Box component="span" sx={{ color: c.subtle }}>
                    Unassigned
                  </Box>
                ),
            },
          ]
        : []),
      {
        Header: "Total",
        accessor: "total",
        align: "right" as const,
        Cell: ({ value }: { value: number }) => (
          <Box component="span" sx={{ fontWeight: 700, color: c.text }}>
            {formatMoney(value)}
          </Box>
        ),
      },
      {
        Header: "Actions",
        id: "actions",
        accessor: "orderNumber",
        align: "right" as const,
        disableSortBy: true,
        disableGlobalFilter: true,
        Cell: ({ row }: CellProps) => (
          <Box sx={{ display: "flex", justifyContent: "flex-end", alignItems: "center", gap: 1 }}>
            {isTenantAdmin && statusActions(row.original.order)}
            <Tooltip title="View details">
              <IconButton
                size="small"
                aria-label={`View order ${row.original.orderNumber}`}
                onClick={() => setDetailId(row.original.order.id)}
                sx={{ color: c.muted }}
              >
                <Icon fontSize="small">visibility</Icon>
              </IconButton>
            </Tooltip>
          </Box>
        ),
      },
    ];
    return { columns, rows };
  }, [orders, statusFilter, isTenantAdmin, assigneeName, statusActions, c]);

  const filterOptions = [
    { value: "all", label: "All", count: orders?.length },
    ...ORDER_STATUSES.map((status) => ({
      value: status,
      label: ORDER_STATUS_LABELS[status],
      count: orders?.filter((o) => o.status === status).length,
    })),
  ];

  const detail = orders?.find((o) => o.id === detailId) ?? null;

  const estimatedTotal = items.reduce((sum, item) => {
    const product = products.find((p) => p.id === item.productId);
    const quantity = Number(item.quantity);
    return product && quantity > 0 ? sum + product.price * quantity : sum;
  }, 0);

  const updateItem = (index: number, patch: Partial<ItemRow>) => {
    const next = [...items];
    next[index] = { ...next[index], ...patch };
    setItems(next);
  };

  return (
    <PageShell>
      <PageHeader
        icon="receipt_long"
        title="Orders"
        subtitle={
          isTenantAdmin
            ? "Create orders, assign them, and move them through fulfilment."
            : "The orders assigned to you. Contact an administrator to change one."
        }
        actions={
          isTenantAdmin && (
            <MDButton variant="gradient" color="info" onClick={openCreate} startIcon={<Icon>add</Icon>}>
              Create order
            </MDButton>
          )
        }
      />

      <Section flush>
        {loading && <StateBlock kind="loading" title="Loading orders" />}
        {error && (
          <StateBlock
            kind="error"
            title="Orders could not be loaded"
            message={error}
            action={
              <MDButton variant="outlined" color="info" size="small" onClick={load}>
                Try again
              </MDButton>
            }
          />
        )}
        {!loading && !error && orders?.length === 0 && (
          <StateBlock
            icon="receipt_long"
            title="No orders yet"
            message={isTenantAdmin ? "Create the first order to get started." : "No orders are currently assigned to you."}
            action={
              isTenantAdmin && (
                <MDButton variant="gradient" color="info" size="small" onClick={openCreate}>
                  Create order
                </MDButton>
              )
            }
          />
        )}
        {!loading && !error && (orders?.length ?? 0) > 0 && (
          <>
            <Box sx={{ px: 3, pt: 2.5 }}>
              <FilterTabs label="Filter by status" value={statusFilter} onChange={setStatusFilter} options={filterOptions} />
            </Box>
            <DataTable table={table} canSearch />
          </>
        )}
      </Section>

      <KitDialog
        open={!!detail}
        onClose={() => setDetailId(null)}
        icon="receipt_long"
        title={detail ? `Order ${detail.orderNumber}` : ""}
        subtitle={detail ? `Created ${formatDateTime(detail.createdAtUtc)}` : undefined}
        maxWidth="md"
        actions={
          <>
            {detail && isTenantAdmin && statusActions(detail)}
            <MDButton variant="text" color="secondary" onClick={() => setDetailId(null)}>
              Close
            </MDButton>
          </>
        }
      >
        {detail && (
          <>
            <DetailList
              columns={3}
              items={[
                {
                  label: "Status",
                  value: <StatusPill tone={ORDER_STATUS_TONE[detail.status]} label={ORDER_STATUS_LABELS[detail.status]} />,
                },
                ...(isTenantAdmin ? [{ label: "Assigned to", value: assigneeName(detail.assignedUserId) }] : []),
                { label: "Last updated", value: formatDateTime(detail.updatedAtUtc) },
              ]}
            />
            <Box sx={{ mt: 3, border: `1px solid ${c.border}`, borderRadius: "12px", overflow: "hidden" }}>
              {detail.items.map((line, index) => (
                <Box
                  key={index}
                  sx={{
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "space-between",
                    gap: 2,
                    px: 2,
                    py: 1.5,
                    borderBottom: `1px solid ${c.border}`,
                  }}
                >
                  <Box sx={{ minWidth: 0, lineHeight: 1.35 }}>
                    <Box sx={{ fontSize: "0.875rem", fontWeight: 500, color: c.text }}>{line.productName}</Box>
                    <Box sx={{ fontFamily: "monospace", fontSize: "0.75rem", color: c.muted }}>{line.productSku}</Box>
                  </Box>
                  <Box sx={{ textAlign: "right", lineHeight: 1.35 }}>
                    <Box sx={{ fontSize: "0.875rem", fontWeight: 500, color: c.text }}>
                      {formatMoney(line.unitPrice * line.quantity)}
                    </Box>
                    <Box sx={{ fontSize: "0.75rem", color: c.muted }}>
                      {line.quantity} × {formatMoney(line.unitPrice)}
                    </Box>
                  </Box>
                </Box>
              ))}
              <Box
                sx={{
                  display: "flex",
                  justifyContent: "space-between",
                  px: 2,
                  py: 1.5,
                  fontWeight: 700,
                  color: c.text,
                  backgroundColor: c.surfaceAlt,
                }}
              >
                <span>Total</span>
                <span>{formatMoney(detail.total)}</span>
              </Box>
            </Box>
          </>
        )}
      </KitDialog>

      <KitDialog
        open={formOpen}
        onClose={() => setFormOpen(false)}
        onSubmit={submitForm}
        icon="add_shopping_cart"
        title="Create order"
        subtitle="Prices are taken from the catalog when the order is saved."
        actions={
          <>
            <MDButton variant="text" color="secondary" onClick={() => setFormOpen(false)}>
              Cancel
            </MDButton>
            <MDButton type="submit" variant="gradient" color="info">
              Create
            </MDButton>
          </>
        }
      >
        {formError && <InlineAlert sx={{ mb: 2.5 }}>{formError}</InlineAlert>}
        {items.map((item, index) => (
          <Box key={index} sx={{ display: "flex", alignItems: "center", gap: 1, mb: 2, pt: index === 0 ? 0.5 : 0 }}>
            <MDInput
              select
              label="Product"
              fullWidth
              SelectProps={{ native: true }}
              InputLabelProps={{ shrink: true }}
              value={item.productId}
              onChange={(e: React.ChangeEvent<HTMLInputElement>) => updateItem(index, { productId: e.target.value })}
            >
              <option value="">Select a product</option>
              {products.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.sku} — {p.name} ({formatMoney(p.price)})
                </option>
              ))}
            </MDInput>
            <MDInput
              label="Qty"
              type="number"
              inputProps={{ min: 1, step: 1 }}
              sx={{ width: "6rem", flexShrink: 0 }}
              value={item.quantity}
              onChange={(e: React.ChangeEvent<HTMLInputElement>) => updateItem(index, { quantity: e.target.value })}
            />
            <IconButton
              size="small"
              onClick={() => setItems(items.filter((_, i) => i !== index))}
              disabled={items.length === 1}
              aria-label="Remove item"
              sx={{ color: c.muted }}
            >
              <Icon fontSize="small">delete</Icon>
            </IconButton>
          </Box>
        ))}
        <Box sx={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 2, mb: 2.5 }}>
          <MDButton
            size="small"
            variant="text"
            color="info"
            startIcon={<Icon>add</Icon>}
            onClick={() => setItems([...items, { productId: "", quantity: "1" }])}
          >
            Add item
          </MDButton>
          <Box sx={{ fontSize: "0.875rem", color: c.muted }}>
            Estimated total{" "}
            <Box component="span" sx={{ ml: 0.5, fontWeight: 700, color: c.text }}>
              {formatMoney(estimatedTotal)}
            </Box>
          </Box>
        </Box>
        <MDInput
          select
          label="Assign to"
          fullWidth
          SelectProps={{ native: true }}
          InputLabelProps={{ shrink: true }}
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
      </KitDialog>
    </PageShell>
  );
}
