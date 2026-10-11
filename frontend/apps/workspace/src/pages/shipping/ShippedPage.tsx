import { useCallback, useMemo, useState } from "react";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import { FilterTabs, PageHeader, Section, SimpleTable, StatCard, StateBlock, downloadCsv, formatDateTime, formatMoney, timeAgo, useKit } from "examples/Kit";
import PageShell from "../../components/PageShell";
import { useLoaded } from "../../lib/useLoaded";
import { ShippingApi, type ShippedOrder } from "../../api/tools";

type Span = "7" | "30" | "90";
type Change = React.ChangeEvent<HTMLInputElement>;

// What has gone out, with who carried it and the tracking number, to answer "where is my order".
export default function ShippedPage() {
  const { c } = useKit();
  const [span, setSpan] = useState<Span>("30");
  const [search, setSearch] = useState("");
  const read = useCallback(() => ShippingApi.shipped(Number(span)), [span]);
  const { data, error, loading } = useLoaded<ShippedOrder[]>(read, "The shipped orders could not be loaded.");

  const rows = useMemo(() => {
    const wanted = search.trim().toLowerCase();
    return (data ?? []).filter((o) => !wanted || `${o.orderNumber} ${o.trackingNumber ?? ""} ${o.carrier ?? ""} ${o.channel}`.toLowerCase().includes(wanted));
  }, [data, search]);
  const untracked = (data ?? []).filter((o) => !o.trackingNumber).length;

  const exportCsv = () =>
    downloadCsv(
      `shipped-${span}-days.csv`,
      ["Order", "Sales channel", "Shipped", "Carrier", "Tracking number", "Units", "Total"],
      rows.map((o) => [o.orderNumber, o.channel, o.shippedAtUtc, o.carrier ?? "", o.trackingNumber ?? "", o.units, o.total.toFixed(2)])
    );

  return (
    <PageShell>
      <PageHeader
        icon="inventory"
        title="Shipped"
        subtitle="Orders marked shipped from here, with who carried each and its tracking number."
        actions={
          <MDButton variant="outlined" color="info" disabled={rows.length === 0} onClick={exportCsv} startIcon={<Icon>download</Icon>}>
            Export (CSV)
          </MDButton>
        }
      />

      {error && <StateBlock kind="error" title="The shipped orders could not be loaded" message={error} />}
      {loading && !data && <StateBlock kind="loading" title="Loading what was shipped" />}

      {data && (
        <Box sx={{ display: "grid", gap: 3 }}>
          <Box sx={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: 3 }}>
            <StatCard icon="inventory" tone="success" label="Shipped" value={data.length.toLocaleString()} hint={`In the last ${span} days`} />
            <StatCard icon="shopping_basket" tone="primary" label="Units" value={data.reduce((sum, o) => sum + o.units, 0).toLocaleString()} hint={formatMoney(data.reduce((sum, o) => sum + o.total, 0))} />
            <StatCard icon="help_outline" tone={untracked > 0 ? "warning" : "success"} label="Without tracking" value={untracked.toLocaleString()} hint="Nothing to look up if the customer asks" />
          </Box>

          <Section
            flush
            icon="list_alt"
            title="Shipments"
            subtitle="An order completed from the Orders page, not from Shipping, is not listed here."
            actions={
              <FilterTabs
                label="Span"
                value={span}
                onChange={setSpan}
                options={[
                  { value: "7", label: "7 days" },
                  { value: "30", label: "30 days" },
                  { value: "90", label: "90 days" },
                ]}
              />
            }
          >
            <Box sx={{ px: 3, pt: 2 }}>
              <MDInput label="Find an order, a tracking number or a carrier" fullWidth value={search} onChange={(e: Change) => setSearch(e.target.value)} />
            </Box>
            <SimpleTable
              rows={rows.slice(0, 300)}
              getRowId={(row: ShippedOrder) => row.id}
              emptyMessage={data.length === 0 ? "Nothing has been shipped from here in this span." : "Nothing matches that."}
              columns={[
                {
                  key: "order",
                  header: "Order",
                  render: (row: ShippedOrder) => (
                    <Box sx={{ lineHeight: 1.35 }}>
                      <Box sx={{ fontWeight: 500, color: c.text }}>{row.orderNumber}</Box>
                      <Box sx={{ fontSize: "0.75rem", color: c.muted }}>{row.channel}</Box>
                    </Box>
                  ),
                },
                { key: "shipped", header: "Shipped", render: (row: ShippedOrder) => <span title={formatDateTime(row.shippedAtUtc)}>{timeAgo(row.shippedAtUtc)}</span> },
                { key: "carrier", header: "Carrier", render: (row: ShippedOrder) => row.carrier ?? "—" },
                { key: "tracking", header: "Tracking number", render: (row: ShippedOrder) => <Box sx={{ fontFamily: "monospace", fontSize: "0.8125rem" }}>{row.trackingNumber ?? "—"}</Box> },
                { key: "units", header: "Units", align: "right", render: (row: ShippedOrder) => row.units.toLocaleString() },
                { key: "total", header: "Total", align: "right", render: (row: ShippedOrder) => formatMoney(row.total) },
              ]}
            />
          </Section>
        </Box>
      )}
    </PageShell>
  );
}
