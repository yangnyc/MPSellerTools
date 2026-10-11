import { useMemo, useState } from "react";
import { Link as RouterLink } from "react-router-dom";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import MDButton from "components/MDButton";
import { FilterTabs, PageHeader, Section, SimpleTable, StatCard, StateBlock, StatusPill, downloadCsv, formatMoney, useKit } from "examples/Kit";
import PageShell from "../../components/PageShell";
import { useLoaded } from "../../lib/useLoaded";
import { ReportsApi, type InventoryReport, type InventoryReportItem } from "../../api/tools";

type Filter = "running-out" | "out" | "not-selling" | "all";
const SHOWN = 200;

const read = () => ReportsApi.inventory();

// What is on the shelves, what it is worth, and what runs out or sits still at the pace things sell.
export default function InventoryReportPage() {
  const { c } = useKit();
  const { data, error, loading } = useLoaded<InventoryReport>(read, "The inventory report could not be loaded.");
  const [filter, setFilter] = useState<Filter>("running-out");

  const groups = useMemo(() => {
    const items = data?.items ?? [];
    return {
      // Selling, and gone within a month at the pace of the last thirty days.
      "running-out": items.filter((i) => i.onHand > 0 && i.daysOfStock !== null && i.daysOfStock <= 30),
      out: items.filter((i) => i.availableToSell <= 0),
      "not-selling": items.filter((i) => i.onHand > 0 && i.soldLast30Days === 0),
      all: items,
    } satisfies Record<Filter, InventoryReportItem[]>;
  }, [data]);
  const rows = groups[filter];

  const exportCsv = () =>
    downloadCsv(
      `inventory-${filter}.csv`,
      ["SKU", "Name", "Category", "Price", "On hand", "Reserved", "Available to sell", "Stock value", "Sold in 30 days", "Days of stock"],
      rows.map((i) => [i.sku, i.name, i.category ?? "", i.price.toFixed(2), i.onHand, i.reserved, i.availableToSell, i.value.toFixed(2), i.soldLast30Days, i.daysOfStock ?? ""])
    );

  return (
    <PageShell>
      <PageHeader
        icon="warehouse"
        title="Inventory report"
        subtitle="What is on the shelves, what it is worth, and what runs out or sits still at the pace things have sold in the last 30 days."
        actions={
          <MDButton variant="outlined" color="info" disabled={!data || rows.length === 0} onClick={exportCsv} startIcon={<Icon>download</Icon>}>
            Export this list (CSV)
          </MDButton>
        }
      />

      {error && <StateBlock kind="error" title="The report could not be loaded" message={error} />}
      {loading && !data && <StateBlock kind="loading" title="Counting the stock" />}

      {data && (
        <Box sx={{ display: "grid", gap: 3 }}>
          <Box sx={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: 3 }}>
            <StatCard icon="inventory_2" tone="primary" label="Stock value" value={formatMoney(data.value)} hint={`${data.unitsOnHand.toLocaleString()} units of ${data.products.toLocaleString()} products, at their prices`} />
            <StatCard icon="remove_shopping_cart" tone={data.outOfStock > 0 ? "error" : "success"} label="Nothing to sell" value={data.outOfStock.toLocaleString()} hint="Out of stock, or all of it held" />
            <StatCard icon="production_quantity_limits" tone={data.lowStock > 0 ? "warning" : "success"} label="Low stock" value={data.lowStock.toLocaleString()} hint={`${data.lowStockThreshold} or fewer left to sell`} to="/inventory" />
            <StatCard icon="hourglass_empty" tone={data.notSelling > 0 ? "warning" : "success"} label="Not selling" value={data.notSelling.toLocaleString()} hint="In stock, none sold in 30 days" />
          </Box>

          <Section
            flush
            icon="list_alt"
            title="Products"
            subtitle={rows.length > SHOWN ? `The first ${SHOWN} of ${rows.length.toLocaleString()}; the export has them all.` : `${rows.length.toLocaleString()} product(s).`}
            actions={
              <FilterTabs
                label="Which products"
                value={filter}
                onChange={setFilter}
                options={[
                  { value: "running-out", label: "Running out", count: groups["running-out"].length },
                  { value: "out", label: "Nothing to sell", count: groups.out.length },
                  { value: "not-selling", label: "Not selling", count: groups["not-selling"].length },
                  { value: "all", label: "All", count: groups.all.length },
                ]}
              />
            }
          >
            <SimpleTable
              rows={rows.slice(0, SHOWN)}
              getRowId={(row: InventoryReportItem) => row.productId}
              emptyMessage={filter === "running-out" ? "Nothing that sells is about to run out." : "No products here."}
              columns={[
                {
                  key: "product",
                  header: "Product",
                  render: (row: InventoryReportItem) => (
                    <Box component={RouterLink} to={`/products/${row.productId}`} sx={{ display: "block", color: "inherit", lineHeight: 1.35 }}>
                      <Box sx={{ fontWeight: 500, color: c.text }}>{row.name}</Box>
                      <Box sx={{ fontSize: "0.75rem", color: c.muted }}>{[row.sku, row.category].filter(Boolean).join(" · ")}</Box>
                    </Box>
                  ),
                },
                { key: "onHand", header: "On hand", align: "right", render: (row: InventoryReportItem) => row.onHand.toLocaleString() },
                { key: "toSell", header: "To sell", align: "right", render: (row: InventoryReportItem) => row.availableToSell.toLocaleString() },
                { key: "sold", header: "Sold, 30 days", align: "right", render: (row: InventoryReportItem) => row.soldLast30Days.toLocaleString() },
                {
                  key: "lasts",
                  header: "Lasts",
                  align: "right",
                  render: (row: InventoryReportItem) =>
                    row.daysOfStock === null ? (
                      <Box component="span" sx={{ color: c.muted }}>not selling</Box>
                    ) : (
                      <StatusPill tone={row.daysOfStock <= 7 ? "error" : row.daysOfStock <= 30 ? "warning" : "success"} label={row.daysOfStock > 365 ? "over a year" : `${row.daysOfStock} days`} />
                    ),
                },
                { key: "value", header: "Stock value", align: "right", render: (row: InventoryReportItem) => formatMoney(row.value) },
              ]}
            />
          </Section>
        </Box>
      )}
    </PageShell>
  );
}
