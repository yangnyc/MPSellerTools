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
import { marketplaces, settingsPath } from "../api/channels";

// Something that wants doing, said with what to do about it and where.
type Attention = { key: string; tone: "error" | "warning" | "info"; icon: string; what: string; why: string; action: string; to: string };

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
  const workspace = data?.workspace ?? null;
  const count = (n: number, one: string, many: string) => `${n.toLocaleString()} ${n === 1 ? one : many}`;
  // A channel's own pages, for the ones that have them (the company's website has none).
  const pageOf = (kind: number) => marketplaces().find((m) => m.kind === kind);

  // Everything that wants doing, the most pressing first, each with what to do about it.
  const attention: Attention[] = [];
  if (sales && workspace) {
    if (sales.failedSyncJobs > 0) {
      attention.push({
        key: "sync", tone: "error", icon: "sync_problem",
        what: `${count(sales.failedSyncJobs, "listing was", "listings were")} not accepted by a marketplace`,
        why: "Open each one in the sync queue to read the marketplace's reason, correct the listing, and send it again.",
        action: "Open sync queue", to: "/sync",
      });
    }
    if (sales.openOrderIssues > 0) {
      attention.push({
        key: "orders", tone: "error", icon: "report",
        what: `${count(sales.openOrderIssues, "order line", "order lines")} could not be matched to a product`,
        why: "The SKU on the order is not in your catalog, or there was not enough stock. Add the product or correct the stock, then clear the line in the sync queue.",
        action: "Open sync queue", to: "/sync",
      });
    }
    if (workspace.jobsNeedingALook > 0) {
      attention.push({
        key: "jobs", tone: "warning", icon: "playlist_remove",
        what: `${count(workspace.jobsNeedingALook, "background job", "background jobs")} failed or held items back in the last ${workspace.jobDays} days`,
        why: "Open the job to see which items were held back and why. Once that is put right, Run again picks up what is left.",
        action: "Open jobs", to: "/jobs",
      });
    }
    sales.staleChannels.forEach((name) => {
      const account = workspace.channels.find((ch) => ch.name === name);
      const page = account && pageOf(account.channel);
      attention.push({
        key: `stale-${name}`, tone: "warning", icon: "schedule",
        what: `Orders have not been read from ${name} recently`,
        why: "Until they are, stock is not sent to it. Check that Import orders is on and the connection still works.",
        action: `Open ${name} settings`, to: page ? settingsPath(page) : "/sync",
      });
    });
    workspace.channels.filter((ch) => ch.isEnabled && !ch.liveWrites && ch.listings > 0).forEach((ch) => {
      const page = pageOf(ch.channel);
      attention.push({
        key: `dry-${ch.id}`, tone: "info", icon: "science",
        what: `${ch.name} is in dry-run mode`,
        why: `Its ${count(ch.listings, "listing is", "listings are")} prepared and checked, but nothing is sent. Switch on Live writes in its settings when you are ready to sell there.`,
        action: `Open ${ch.name} settings`, to: page ? settingsPath(page) : "/sync",
      });
    });
    if (workspace.catalog.noPrice > 0) {
      attention.push({
        key: "price", tone: "warning", icon: "sell",
        what: `${count(workspace.catalog.noPrice, "product has", "products have")} no price`,
        why: "A product imported from Amazon starts at 0 when Amazon has no list price. Set its price before publishing it.",
        action: "Open products", to: "/products",
      });
    }
    if (sales.lowStockCount > 0) {
      attention.push({
        key: "stock", tone: "warning", icon: "production_quantity_limits",
        what: `${count(sales.lowStockCount, "item has", "items have")} ${sales.lowStockThreshold} or fewer left to sell`,
        why: "Count what is on the shelf and enter it under Stock, or reorder.",
        action: "Open stock", to: "/inventory",
      });
    }
  }
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
            ? "What needs you today, how sales are going, and how your channels, jobs and catalog stand."
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
            {isTenantAdmin && (
              <MDButton component={RouterLink} to="/import/amazon" variant="outlined" color="white" startIcon={<Icon>download</Icon>}>
                Import
              </MDButton>
            )}
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
            icon="payments"
            tone="success"
            label={`Revenue, ${sales?.days ?? 30} days`}
            value={sales ? formatMoney(sales.revenue) : undefined}
            hint={sales ? count(sales.orders, "order", "orders") : undefined}
            to="/orders"
          />
        )}
        {isTenantAdmin && (
          <StatCard
            icon="inventory_2"
            tone="primary"
            label="Catalog"
            value={data?.productCount}
            hint={workspace ? `${workspace.productsAddedThisWeek.toLocaleString()} added this week` : "Active products"}
            to="/products"
          />
        )}
        {isTenantAdmin && (
          <StatCard
            icon="storefront"
            tone="info"
            label="On sale"
            value={sales?.listings.live}
            hint={sales ? `${count(sales.listings.draft, "draft", "drafts")} waiting to be published` : undefined}
            to="/listings"
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
          {workspace && (
            <Box sx={{ mb: 3 }}>
              <Section
                icon={attention.length > 0 ? "notification_important" : "task_alt"}
                tone={attention.some((a) => a.tone === "error") ? "error" : attention.length > 0 ? "warning" : "success"}
                title="Needs your attention"
                subtitle={attention.length > 0 ? "What wants doing, the most pressing first, and what to do about each." : "Nothing is waiting on you."}
                flush
              >
                {attention.length === 0 && <StateBlock icon="task_alt" title="All clear" message="Your sales channels, jobs, orders and stock have nothing that needs you right now." />}
                {attention.map((item) => (
                  <Box key={item.key} sx={{ ...rowSx, alignItems: "flex-start", flexWrap: "wrap" }}>
                    <Box sx={{ display: "flex", alignItems: "flex-start", gap: 1.5, flex: 1, minWidth: 240 }}>
                      <Icon fontSize="small" sx={{ mt: 0.25, color: tone(item.tone).solid }}>{item.icon}</Icon>
                      <Box sx={{ minWidth: 0 }}>
                        <Box sx={{ fontSize: "0.875rem", fontWeight: 500, color: c.text }}>{item.what}</Box>
                        <Box sx={{ mt: 0.25, fontSize: "0.8125rem", lineHeight: 1.5, color: c.muted }}>{item.why}</Box>
                      </Box>
                    </Box>
                    <MDButton component={RouterLink} to={item.to} variant="outlined" color="info" size="small">
                      {item.action}
                    </MDButton>
                  </Box>
                ))}
              </Section>
            </Box>
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

              <Section icon="storefront" title="Sales channels" subtitle="Where you sell, and how the listings there stand" actions={viewAll("/listings")} flush>
                {workspace?.channels.length === 0 && (
                  <StateBlock icon="storefront" title="No sales channels yet" message="Add eBay, Amazon, Walmart or your Magento store from its menu to start listing products." />
                )}
                {workspace?.channels.map((channel) => {
                  const page = pageOf(channel.channel);
                  return (
                    <Box key={channel.id} sx={rowSx}>
                      <Box sx={{ minWidth: 0 }}>
                        <Box
                          component={page ? RouterLink : "div"}
                          {...(page ? { to: page.path } : {})}
                          sx={{ fontSize: "0.875rem", fontWeight: 500, color: c.text, "&:hover": page ? { color: c.accent } : undefined }}
                        >
                          {channel.name}
                        </Box>
                        <Box sx={{ fontSize: "0.75rem", color: c.muted }}>
                          {[
                            `${channel.live.toLocaleString()} live`,
                            channel.drafts > 0 && count(channel.drafts, "draft", "drafts"),
                            channel.rejected > 0 && `${channel.rejected.toLocaleString()} rejected`,
                            channel.orderImport && "orders imported",
                            channel.stockSync && "stock sent",
                          ]
                            .filter(Boolean)
                            .join(" · ")}
                        </Box>
                      </Box>
                      <StatusPill
                        tone={!channel.isEnabled ? "neutral" : channel.liveWrites ? "success" : "warning"}
                        label={!channel.isEnabled ? "Switched off" : channel.liveWrites ? "Live" : "Dry run"}
                      />
                    </Box>
                  );
                })}
                <Box sx={{ display: "flex", flexWrap: "wrap", gap: 1, px: 3, py: 1.75, borderTop: workspace && workspace.channels.length > 0 ? `1px solid ${c.border}` : undefined }}>
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

      {workspace && (
        <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", lg: "1fr 1fr" }, gap: 3, mb: 3 }}>
          <Section icon="playlist_play" title="Background jobs" subtitle="Large pieces of work carried out for you: publishing, reading a store, importing" actions={viewAll("/jobs")}>
            <Box sx={{ display: "flex", flexWrap: "wrap", gap: 1 }}>
              <StatusPill tone="info" pulse={workspace.jobsRunning > 0} label={`${workspace.jobsRunning} running`} />
              <StatusPill tone="neutral" label={`${workspace.jobsWaiting} waiting`} />
              <StatusPill tone={workspace.jobsNeedingALook > 0 ? "warning" : "success"} label={`${workspace.jobsNeedingALook} need a look`} />
            </Box>
            <Box sx={{ mt: 1.5, fontSize: "0.8125rem", lineHeight: 1.5, color: c.muted }}>
              {workspace.lastJobSummary ? `Last finished: ${workspace.lastJobSummary}` : `No job has finished in the last ${workspace.jobDays} days.`}
            </Box>
          </Section>

          <Section
            icon="fact_check"
            title="Catalog readiness"
            subtitle="What your products still lack before they sell well"
            actions={
              <MDButton component={RouterLink} to="/import/amazon/bulk" variant="text" color="info" size="small">
                Import from Amazon
              </MDButton>
            }
          >
            <Box sx={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(130px, 1fr))", gap: 2 }}>
              {[
                { label: "No price", value: workspace.catalog.noPrice },
                { label: "Nothing in stock", value: workspace.catalog.noStock },
                { label: "No category", value: workspace.catalog.noCategory },
                { label: "No picture", value: workspace.catalog.noPictures },
              ].map((gap) => (
                <Box key={gap.label} component={RouterLink} to="/products" sx={{ display: "block", color: "inherit" }}>
                  <Box sx={{ fontSize: "1.5rem", fontWeight: 700, lineHeight: 1.2, color: gap.value > 0 ? tone("warning").solid : c.text }}>{gap.value.toLocaleString()}</Box>
                  <Box sx={{ fontSize: "0.8125rem", color: c.muted }}>{gap.label}</Box>
                </Box>
              ))}
            </Box>
            <Box sx={{ mt: 1.5, fontSize: "0.8125rem", lineHeight: 1.5, color: c.muted }}>
              Of {(data?.productCount ?? 0).toLocaleString()} active products. A product with no category goes to the store's default one; one with no picture is refused by most marketplaces.
            </Box>
          </Section>
        </Box>
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
