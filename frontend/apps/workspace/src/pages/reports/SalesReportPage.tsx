import { useCallback, useState } from "react";
import { Link as RouterLink } from "react-router-dom";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import Tooltip from "@mui/material/Tooltip";
import MDButton from "components/MDButton";
import { FilterTabs, PageHeader, Section, SimpleTable, StatCard, StateBlock, downloadCsv, formatMoney, useKit } from "examples/Kit";
import PageShell from "../../components/PageShell";
import { useLoaded } from "../../lib/useLoaded";
import { ReportsApi, type SalesReport } from "../../api/tools";

type Span = "7" | "30" | "90" | "365";

// A calendar day as the server names it ("2026-10-06"), shown without shifting it into the viewer's time zone.
const day = (date: string) => new Date(`${date}T00:00:00Z`).toLocaleDateString(undefined, { month: "short", day: "numeric", timeZone: "UTC" });

// How this span compares with the one before it, in words.
function against(now: number, before: number) {
  if (before === 0) return now === 0 ? "the same as the span before" : "nothing in the span before to compare with";
  const change = ((now - before) / before) * 100;
  return `${change >= 0 ? "up" : "down"} ${Math.abs(change).toFixed(0)}% on the span before`;
}

// What sold, when, where and which products, over a chosen span.
export default function SalesReportPage() {
  const { c, tone } = useKit();
  const [span, setSpan] = useState<Span>("30");
  const read = useCallback(() => ReportsApi.sales(Number(span)), [span]);
  const { data, error, loading } = useLoaded<SalesReport>(read, "The sales report could not be loaded.");

  const peak = Math.max(1, ...(data?.daily.map((d) => d.revenue) ?? []));
  const exportCsv = () => {
    if (!data) return;
    downloadCsv(
      `sales-${data.days}-days.csv`,
      ["Date", "Orders", "Units", "Revenue"],
      data.daily.map((d) => [d.date, d.orders, d.units, d.revenue.toFixed(2)])
    );
  };

  return (
    <PageShell>
      <PageHeader
        icon="payments"
        title="Sales report"
        subtitle="What sold, when, through which sales channel, and which products. Cancelled orders are left out."
        actions={
          <MDButton variant="outlined" color="info" disabled={!data} onClick={exportCsv} startIcon={<Icon>download</Icon>}>
            Export days (CSV)
          </MDButton>
        }
      />
      <Box sx={{ mb: 3 }}>
        <FilterTabs
          label="Span of the report"
          value={span}
          onChange={setSpan}
          options={[
            { value: "7", label: "7 days" },
            { value: "30", label: "30 days" },
            { value: "90", label: "90 days" },
            { value: "365", label: "A year" },
          ]}
        />
      </Box>

      {error && <StateBlock kind="error" title="The report could not be loaded" message={error} />}
      {loading && !data && <StateBlock kind="loading" title="Adding up the sales" />}

      {data && (
        <Box sx={{ display: "grid", gap: 3 }}>
          <Box sx={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: 3 }}>
            <StatCard icon="payments" tone="success" label="Revenue" value={formatMoney(data.revenue)} hint={against(data.revenue, data.previousRevenue)} />
            <StatCard icon="receipt_long" tone="info" label="Orders" value={data.orders.toLocaleString()} hint={against(data.orders, data.previousOrders)} />
            <StatCard icon="shopping_basket" tone="primary" label="Average order" value={formatMoney(data.averageOrder)} hint={`${data.units.toLocaleString()} units in all`} />
            <StatCard icon="cancel" tone={data.cancelled > 0 ? "warning" : "neutral"} label="Cancelled" value={data.cancelled.toLocaleString()} hint="Not counted above" to="/orders" />
          </Box>

          <Section icon="bar_chart" title={`Revenue by day, last ${data.days} days`} subtitle={`Best day ${formatMoney(peak)}. Hover a bar for its day.`}>
            {data.orders === 0 ? (
              <Box sx={{ fontSize: "0.875rem", color: c.muted }}>No orders in this span.</Box>
            ) : (
              <>
                <Box
                  role="img"
                  aria-label={`Revenue per day over the last ${data.days} days, ${formatMoney(data.revenue)} in total. The table of days can be exported.`}
                  sx={{ display: "flex", alignItems: "flex-end", gap: data.days > 120 ? 0 : "2px", height: 160, borderBottom: `1px solid ${c.border}` }}
                >
                  {data.daily.map((d) => (
                    <Tooltip key={d.date} title={`${day(d.date)}: ${formatMoney(d.revenue)} · ${d.orders} ${d.orders === 1 ? "order" : "orders"}`} placement="top">
                      <Box sx={{ flex: 1, height: "100%", display: "flex", alignItems: "flex-end", "&:hover > div": { opacity: 0.7 } }}>
                        <Box sx={{ width: "100%", height: d.revenue > 0 ? `max(3px, ${(d.revenue / peak) * 100}%)` : 0, borderRadius: "3px 3px 0 0", backgroundColor: tone("info").solid }} />
                      </Box>
                    </Tooltip>
                  ))}
                </Box>
                <Box sx={{ display: "flex", justifyContent: "space-between", mt: 0.75, fontSize: "0.75rem", color: c.muted }}>
                  <span>{day(data.daily[0].date)}</span>
                  <span>{day(data.daily[data.daily.length - 1].date)}</span>
                </Box>
              </>
            )}
          </Section>

          <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", lg: "2fr 3fr" }, alignItems: "start", gap: 3 }}>
            <Section flush icon="storefront" title="By sales channel" subtitle="Where the orders came from.">
              <SimpleTable
                rows={data.channels}
                getRowId={(row: SalesReport["channels"][number]) => row.channel}
                emptyMessage="No orders in this span."
                columns={[
                  { key: "channel", header: "Channel", render: (row: SalesReport["channels"][number]) => row.channel },
                  { key: "orders", header: "Orders", align: "right", render: (row: SalesReport["channels"][number]) => row.orders.toLocaleString() },
                  { key: "units", header: "Units", align: "right", render: (row: SalesReport["channels"][number]) => row.units.toLocaleString() },
                  { key: "revenue", header: "Revenue", align: "right", render: (row: SalesReport["channels"][number]) => formatMoney(row.revenue) },
                ]}
              />
            </Section>

            <Section flush icon="emoji_events" title="Best sellers" subtitle="The 25 products that brought in the most.">
              <SimpleTable
                rows={data.topProducts}
                getRowId={(row: SalesReport["topProducts"][number]) => row.productId}
                emptyMessage="Nothing sold in this span."
                columns={[
                  {
                    key: "product",
                    header: "Product",
                    render: (row: SalesReport["topProducts"][number]) => (
                      <Box component={RouterLink} to={`/products/${row.productId}`} sx={{ display: "block", color: "inherit", lineHeight: 1.35 }}>
                        <Box sx={{ fontWeight: 500, color: c.text }}>{row.name}</Box>
                        <Box sx={{ fontSize: "0.75rem", color: c.muted }}>{row.sku}</Box>
                      </Box>
                    ),
                  },
                  { key: "units", header: "Units", align: "right", render: (row: SalesReport["topProducts"][number]) => row.units.toLocaleString() },
                  { key: "orders", header: "Orders", align: "right", render: (row: SalesReport["topProducts"][number]) => row.orders.toLocaleString() },
                  { key: "revenue", header: "Revenue", align: "right", render: (row: SalesReport["topProducts"][number]) => formatMoney(row.revenue) },
                ]}
              />
            </Section>
          </Box>
        </Box>
      )}
    </PageShell>
  );
}
