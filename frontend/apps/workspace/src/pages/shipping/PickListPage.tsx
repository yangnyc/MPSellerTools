import { Link as RouterLink } from "react-router-dom";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import MDButton from "components/MDButton";
import { PageHeader, Section, SimpleTable, StatCard, StateBlock, StatusPill, downloadCsv, useKit } from "examples/Kit";
import PageShell from "../../components/PageShell";
import { useLoaded } from "../../lib/useLoaded";
import { ShippingApi, type PickLine } from "../../api/tools";

const read = () => ShippingApi.pickList();

// Everything the open orders need off the shelves, product by product, to fetch in one round.
export default function PickListPage() {
  const { c } = useKit();
  const { data, error, loading } = useLoaded<PickLine[]>(read, "The pick list could not be loaded.");
  const lines = data ?? [];
  const short = lines.filter((line) => line.quantity > line.onHand);

  const exportCsv = () =>
    downloadCsv("pick-list.csv", ["SKU", "Product", "To pick", "On the shelf", "Orders"], lines.map((l) => [l.sku, l.name, l.quantity, l.onHand, l.orderNumbers.join(" ")]));

  return (
    <PageShell>
      <PageHeader
        icon="checklist"
        title="Pick list"
        subtitle="Everything the orders waiting to ship need off the shelves, by SKU, so it can be fetched in one round."
        actions={
          <>
            <MDButton variant="outlined" color="info" disabled={lines.length === 0} onClick={() => window.print()} startIcon={<Icon>print</Icon>}>
              Print
            </MDButton>
            <MDButton variant="outlined" color="info" disabled={lines.length === 0} onClick={exportCsv} startIcon={<Icon>download</Icon>}>
              CSV
            </MDButton>
            <MDButton component={RouterLink} to="/shipping" variant="gradient" color="info" startIcon={<Icon>local_shipping</Icon>}>
              Ready to ship
            </MDButton>
          </>
        }
      />

      {error && <StateBlock kind="error" title="The pick list could not be loaded" message={error} />}
      {loading && !data && <StateBlock kind="loading" title="Adding up what to pick" />}

      {data && (
        <Box sx={{ display: "grid", gap: 3 }}>
          <Box sx={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: 3 }}>
            <StatCard icon="category" tone="info" label="Products to pick" value={lines.length.toLocaleString()} hint="Different SKUs" />
            <StatCard icon="shopping_basket" tone="primary" label="Units to pick" value={lines.reduce((sum, l) => sum + l.quantity, 0).toLocaleString()} hint="For every open order together" />
            <StatCard icon="production_quantity_limits" tone={short.length > 0 ? "error" : "success"} label="Short" value={short.length.toLocaleString()} hint="More wanted than is on the shelf" />
          </Box>

          <Section flush icon="checklist" title="To pick" subtitle="In SKU order, as shelves are usually kept.">
            <SimpleTable
              rows={lines}
              getRowId={(row: PickLine) => row.productId}
              emptyMessage="No order is waiting, so there is nothing to pick."
              columns={[
                { key: "sku", header: "SKU", render: (row: PickLine) => <Box sx={{ fontFamily: "monospace", fontWeight: 700, color: c.text }}>{row.sku}</Box> },
                { key: "name", header: "Product", render: (row: PickLine) => <Box sx={{ whiteSpace: "normal", maxWidth: 420 }}>{row.name}</Box> },
                { key: "quantity", header: "To pick", align: "right", render: (row: PickLine) => <Box sx={{ fontWeight: 700, fontSize: "1rem", color: c.text }}>{row.quantity.toLocaleString()}</Box> },
                {
                  key: "onHand",
                  header: "On the shelf",
                  align: "right",
                  render: (row: PickLine) =>
                    row.quantity > row.onHand ? <StatusPill tone="error" label={`${row.onHand} · short by ${row.quantity - row.onHand}`} /> : row.onHand.toLocaleString(),
                },
                {
                  key: "orders",
                  header: "For orders",
                  render: (row: PickLine) => (
                    <Box sx={{ whiteSpace: "normal", maxWidth: 320, fontSize: "0.75rem", color: c.muted }}>
                      {row.orderNumbers.join(", ")}
                      {row.orders > row.orderNumbers.length && ` and ${row.orders - row.orderNumbers.length} more`}
                    </Box>
                  ),
                },
              ]}
            />
          </Section>
        </Box>
      )}
    </PageShell>
  );
}
