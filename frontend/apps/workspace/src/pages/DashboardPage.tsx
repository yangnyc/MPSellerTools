import { useEffect, useState } from "react";
import { Link as RouterLink } from "react-router-dom";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import Tooltip from "@mui/material/Tooltip";
import MDButton from "components/MDButton";
import {
  Hero,
  InlineAlert,
  Section,
  StatCard,
  StateBlock,
  StatusPill,
  formatDateTime,
  formatMoney,
  timeAgo,
  useKit,
} from "examples/Kit";
import PageShell from "../components/PageShell";
import { useAuth } from "../auth/useAuth";
import { DashboardApi, OrdersApi, TasksApi } from "../api/resources";
import {
  ORDER_STATUS_LABELS,
  TASK_STATUS_LABELS,
  type Order,
  type TenantDashboard,
  type WorkItem,
} from "../api/types";
import { ORDER_STATUS_TONE, TASK_STATUS_TONE } from "../lib/status";

const LIST_LIMIT = 5;

// A calendar day as the server names it ("2026-10-06"), shown without shifting it into the viewer's time zone.
const formatDay = (date: string) =>
  new Date(`${date}T00:00:00Z`).toLocaleDateString(undefined, { month: "short", day: "numeric", timeZone: "UTC" });

export default function DashboardPage() {
  const { user } = useAuth();
  const { c, tone } = useKit();
  const isTenantAdmin = user?.roles.includes("TenantAdmin") ?? false;

  const [data, setData] = useState<TenantDashboard | null>(null);
  const [failed, setFailed] = useState(false);
  // The server already scopes both lists to what this user may see: the whole
  // company for a TenantAdmin, only their own assignments for an Employee.
  const [orders, setOrders] = useState<Order[] | null>(null);
  const [tasks, setTasks] = useState<WorkItem[] | null>(null);

  useEffect(() => {
    DashboardApi.get()
      .then(setData)
      .catch(() => setFailed(true));
    OrdersApi.list()
      .then(setOrders)
      .catch(() => setOrders([]));
    TasksApi.list()
      .then(setTasks)
      .catch(() => setTasks([]));
  }, []);

  const sales = data?.sales ?? null;
  // The tallest bar: every other day is drawn against the best one.
  const peak = Math.max(1, ...(sales?.daily.map((day) => day.revenue) ?? []));

  const firstName = (user?.displayName || user?.email || "").split(/[\s@]/)[0];
  const recentOrders = orders
    ? [...orders].sort((a, b) => b.createdAtUtc.localeCompare(a.createdAtUtc)).slice(0, LIST_LIMIT)
    : null;
  const openTasks = tasks ? tasks.filter((t) => t.status === 0 || t.status === 1).slice(0, LIST_LIMIT) : null;

  const rowSx = {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 2,
    px: 3,
    py: 1.75,
    borderBottom: `1px solid ${c.border}`,
    "&:last-of-type": { borderBottom: "none" },
  };

  const viewAll = (to: string) => (
    <MDButton component={RouterLink} to={to} variant="text" color="info" size="small">
      View all
    </MDButton>
  );

  return (
    <PageShell>
      <Hero
        eyebrow={isTenantAdmin ? "Company Dashboard" : "My Dashboard"}
        title={firstName ? `Welcome back, ${firstName}` : "Welcome back"}
        subtitle={
          isTenantAdmin
            ? "Live numbers from your company's products, orders, and tasks."
            : "The orders and tasks currently assigned to you."
        }
        actions={
          <>
            <MDButton component={RouterLink} to="/orders" color="white" startIcon={<Icon>receipt_long</Icon>}>
              Orders
            </MDButton>
            <MDButton component={RouterLink} to="/tasks" variant="outlined" color="white" startIcon={<Icon>checklist</Icon>}>
              Tasks
            </MDButton>
          </>
        }
      />

      {failed && (
        <InlineAlert sx={{ mb: 3 }}>The dashboard numbers could not be loaded. Reload the page to try again.</InlineAlert>
      )}

      <Box
        sx={{
          display: "grid",
          gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))",
          gap: 3,
          mb: 3,
        }}
      >
        {isTenantAdmin && (
          <StatCard
            icon="inventory_2"
            tone="primary"
            label="Catalog"
            value={data?.productCount}
            hint="Active products"
            to="/products"
          />
        )}
        <StatCard
          icon="receipt_long"
          tone="info"
          label="Open orders"
          value={data?.openOrderCount}
          hint={data ? `of ${data.totalOrderCount} total` : undefined}
          progress={data && data.totalOrderCount > 0 ? data.openOrderCount / data.totalOrderCount : undefined}
          to="/orders"
        />
        <StatCard
          icon="checklist"
          tone="warning"
          label="Open tasks"
          value={data?.openTaskCount}
          hint={data ? `of ${data.totalTaskCount} total` : undefined}
          progress={data && data.totalTaskCount > 0 ? data.openTaskCount / data.totalTaskCount : undefined}
          to="/tasks"
        />
      </Box>

      {sales && (
        <>
          {(sales.failedSyncJobs > 0 || sales.openOrderIssues > 0 || sales.staleChannels.length > 0) && (
            <InlineAlert
              tone="warning"
              title="Your marketplaces need a look"
              sx={{ mb: 3 }}
              action={
                <MDButton component={RouterLink} to="/sync" variant="text" color="info" size="small">
                  Open sync queue
                </MDButton>
              }
            >
              {[
                sales.failedSyncJobs > 0 && `${sales.failedSyncJobs} sync ${sales.failedSyncJobs === 1 ? "job" : "jobs"} failed or need a correction`,
                sales.openOrderIssues > 0 && `${sales.openOrderIssues} order ${sales.openOrderIssues === 1 ? "line" : "lines"} could not be placed`,
                sales.staleChannels.length > 0 && `orders have not been read recently from ${sales.staleChannels.join(", ")}`,
              ]
                .filter(Boolean)
                .join("; ")}
              .
            </InlineAlert>
          )}

          <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", lg: "3fr 2fr" }, gap: 3, mb: 3 }}>
            <Section
              icon="payments"
              tone="success"
              title={`Revenue, last ${sales.days} days`}
              subtitle={`${formatMoney(sales.revenue)} from ${sales.orders} ${sales.orders === 1 ? "order" : "orders"}, cancelled orders left out`}
            >
              {sales.orders === 0 ? (
                <Box sx={{ fontSize: "0.875rem", color: c.muted }}>No orders in this period yet.</Box>
              ) : (
                <>
                  {/* One series, so the title names it and no legend is needed; each bar says its own day on hover. */}
                  <Box
                    role="img"
                    aria-label={`Revenue per day over the last ${sales.days} days, ${formatMoney(sales.revenue)} in total. The table below lists it by channel.`}
                    sx={{ display: "flex", alignItems: "flex-end", gap: "2px", height: 140, borderBottom: `1px solid ${c.border}` }}
                  >
                    {sales.daily.map((day) => (
                      <Tooltip
                        key={day.date}
                        title={`${formatDay(day.date)}: ${formatMoney(day.revenue)} · ${day.orders} ${day.orders === 1 ? "order" : "orders"}`}
                        placement="top"
                      >
                        <Box sx={{ flex: 1, height: "100%", display: "flex", alignItems: "flex-end", "&:hover > div": { opacity: 0.7 } }}>
                          <Box
                            sx={{
                              width: "100%",
                              // A day with sales always shows, however small next to the best day.
                              height: day.revenue > 0 ? `max(3px, ${(day.revenue / peak) * 100}%)` : 0,
                              borderRadius: "4px 4px 0 0",
                              backgroundColor: tone("info").solid,
                            }}
                          />
                        </Box>
                      </Tooltip>
                    ))}
                  </Box>
                  <Box sx={{ display: "flex", justifyContent: "space-between", mt: 0.75, fontSize: "0.75rem", color: c.muted }}>
                    <span>{formatDay(sales.daily[0].date)}</span>
                    <span>Best day {formatMoney(peak)}</span>
                    <span>{formatDay(sales.daily[sales.daily.length - 1].date)}</span>
                  </Box>
                  <Box component="table" sx={{ width: "100%", mt: 2.5, borderCollapse: "collapse", fontSize: "0.875rem", "& td, & th": { py: 0.75 } }}>
                    <thead>
                      <tr>
                        {["Channel", "Orders", "Revenue"].map((heading, i) => (
                          <Box component="th" key={heading} sx={{ textAlign: i === 0 ? "left" : "right", fontSize: "0.6875rem", fontWeight: 700, letterSpacing: "0.06em", textTransform: "uppercase", color: c.subtle }}>
                            {heading}
                          </Box>
                        ))}
                      </tr>
                    </thead>
                    <tbody>
                      {sales.channels.map((channel) => (
                        <Box component="tr" key={channel.channel} sx={{ borderTop: `1px solid ${c.border}` }}>
                          <Box component="td" sx={{ color: c.text, fontWeight: 500 }}>{channel.channel}</Box>
                          <Box component="td" sx={{ textAlign: "right", color: c.muted }}>{channel.orders}</Box>
                          <Box component="td" sx={{ textAlign: "right", color: c.text, fontWeight: 700 }}>{formatMoney(channel.revenue)}</Box>
                        </Box>
                      ))}
                    </tbody>
                  </Box>
                </>
              )}
            </Section>

            <Box sx={{ display: "grid", gap: 3, alignContent: "start" }}>
              <Section
                icon="production_quantity_limits"
                tone="warning"
                title="Low stock"
                subtitle={`${sales.lowStockCount} with ${sales.lowStockThreshold} or fewer left to sell`}
                actions={viewAll("/inventory")}
                flush
              >
                {sales.lowStock.length === 0 && <StateBlock icon="task_alt" title="Nothing is running low" />}
                {sales.lowStock.map((item) => (
                  <Box key={item.variantId} sx={rowSx}>
                    <Box sx={{ minWidth: 0 }}>
                      <Box sx={{ fontSize: "0.875rem", fontWeight: 500, color: c.text }}>{item.productName}</Box>
                      <Box sx={{ fontSize: "0.75rem", color: c.muted }}>{item.sku}</Box>
                    </Box>
                    <StatusPill tone={item.availableToSell === 0 ? "error" : "warning"} label={`${item.availableToSell} left`} />
                  </Box>
                ))}
              </Section>

              <Section icon="sell" title="Marketplace listings" subtitle="As the marketplaces last reported them" actions={viewAll("/sync")}>
                <Box sx={{ display: "flex", flexWrap: "wrap", gap: 1 }}>
                  <StatusPill tone="success" label={`${sales.listings.live} live`} />
                  <StatusPill tone="info" label={`${sales.listings.processing} processing`} />
                  <StatusPill tone="error" label={`${sales.listings.rejected} rejected`} />
                  <StatusPill tone="neutral" label={`${sales.listings.offSale} off sale`} />
                  <StatusPill tone="neutral" label={`${sales.listings.draft} drafts`} />
                </Box>
              </Section>
            </Box>
          </Box>
        </>
      )}

      <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", lg: "1fr 1fr" }, gap: 3 }}>
        <Section
          icon="receipt_long"
          title={isTenantAdmin ? "Recent orders" : "My orders"}
          subtitle="Newest first"
          actions={viewAll("/orders")}
          flush
        >
          {!recentOrders && <StateBlock kind="loading" title="Loading orders" />}
          {recentOrders?.length === 0 && (
            <StateBlock
              icon="receipt_long"
              title="No orders"
              message={isTenantAdmin ? "Orders you create will show up here." : "No orders are currently assigned to you."}
            />
          )}
          {recentOrders?.map((order) => (
            <Box key={order.id} sx={rowSx}>
              <Box sx={{ minWidth: 0 }}>
                <Box sx={{ fontSize: "0.875rem", fontWeight: 500, whiteSpace: "nowrap", color: c.text }}>{order.orderNumber}</Box>
                <Box sx={{ fontSize: "0.75rem", color: c.muted }} title={formatDateTime(order.createdAtUtc)}>
                  {order.items.length} {order.items.length === 1 ? "item" : "items"} · {timeAgo(order.createdAtUtc)}
                </Box>
              </Box>
              <Box sx={{ display: "flex", alignItems: "center", gap: 2 }}>
                <StatusPill tone={ORDER_STATUS_TONE[order.status]} label={ORDER_STATUS_LABELS[order.status]} />
                <Box sx={{ minWidth: 72, textAlign: "right", fontSize: "0.875rem", fontWeight: 700, color: c.text }}>
                  {formatMoney(order.total)}
                </Box>
              </Box>
            </Box>
          ))}
        </Section>

        <Section
          icon="checklist"
          tone="warning"
          title={isTenantAdmin ? "Open tasks" : "My open tasks"}
          subtitle="Not yet done or cancelled"
          actions={viewAll("/tasks")}
          flush
        >
          {!openTasks && <StateBlock kind="loading" title="Loading tasks" />}
          {openTasks?.length === 0 && (
            <StateBlock icon="task_alt" title="All caught up" message="There are no open tasks right now." />
          )}
          {openTasks?.map((task) => (
            <Box key={task.id} sx={rowSx}>
              <Box sx={{ minWidth: 0 }}>
                <Box
                  sx={{
                    overflow: "hidden",
                    textOverflow: "ellipsis",
                    whiteSpace: "nowrap",
                    fontSize: "0.875rem",
                    fontWeight: 500,
                    color: c.text,
                  }}
                >
                  {task.title}
                </Box>
                <Box sx={{ fontSize: "0.75rem", color: c.muted }}>
                  {task.dueAtUtc ? `Due ${formatDateTime(task.dueAtUtc)}` : `Updated ${timeAgo(task.updatedAtUtc)}`}
                </Box>
              </Box>
              <StatusPill tone={TASK_STATUS_TONE[task.status]} label={TASK_STATUS_LABELS[task.status]} />
            </Box>
          ))}
        </Section>
      </Box>
    </PageShell>
  );
}
