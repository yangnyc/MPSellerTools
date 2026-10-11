import { useState } from "react";
import { Link as RouterLink } from "react-router-dom";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import { InlineAlert, KitDialog, PageHeader, Section, SimpleTable, StatCard, StateBlock, StatusPill, formatDateTime, formatMoney, timeAgo, useKit } from "examples/Kit";
import PageShell from "../../components/PageShell";
import { useSnackbar } from "../../components/useSnackbar";
import { ApiError } from "../../lib/api";
import { useLoaded } from "../../lib/useLoaded";
import { ORDER_STATUS_LABELS } from "../../api/types";
import { ORDER_STATUS_TONE } from "../../lib/status";
import { ShippingApi, type ShippingOrder } from "../../api/tools";

type Change = React.ChangeEvent<HTMLInputElement>;
const CARRIERS = ["USPS", "UPS", "FedEx", "DHL", "Local delivery", "Collected by the customer"];

const read = () => ShippingApi.queue();

// The orders waiting to go out, oldest first, and marking one shipped.
export default function ShipQueuePage() {
  const { c } = useKit();
  const { notify } = useSnackbar();
  const { data, error, loading, reload } = useLoaded<ShippingOrder[]>(read, "The orders to ship could not be loaded.");

  const [shipping, setShipping] = useState<ShippingOrder | null>(null);
  const [carrier, setCarrier] = useState(CARRIERS[0]);
  const [tracking, setTracking] = useState("");
  const [busy, setBusy] = useState(false);
  const [shipError, setShipError] = useState<string | null>(null);
  // When the page was opened: what "waiting over two days" is measured from.
  const [openedAt] = useState(() => Date.now());

  const open = (order: ShippingOrder) => {
    setShipping(order);
    setTracking("");
    setShipError(null);
  };

  const ship = async () => {
    if (!shipping) return;
    setBusy(true);
    try {
      await ShippingApi.ship(shipping.id, { carrier, trackingNumber: tracking.trim() });
      notify(`${shipping.orderNumber} marked as shipped${tracking.trim() ? `, tracking ${tracking.trim()}` : ""}.`, "success");
      setShipping(null);
      await reload();
    } catch (err) {
      setShipError(err instanceof ApiError ? err.message : "Could not mark the order as shipped.");
    } finally {
      setBusy(false);
    }
  };

  const orders = data ?? [];
  const short = orders.filter((o) => o.short).length;
  // Waiting more than two days is late for most marketplaces' handling times.
  const late = orders.filter((o) => openedAt - new Date(o.createdAtUtc).getTime() > 2 * 24 * 60 * 60 * 1000).length;

  return (
    <PageShell>
      <PageHeader
        icon="local_shipping"
        title="Ready to ship"
        subtitle="Orders waiting to go out, oldest first. Marking one shipped completes it and records the carrier and tracking number."
        actions={
          <MDButton component={RouterLink} to="/shipping/pick-list" variant="outlined" color="info" startIcon={<Icon>checklist</Icon>}>
            Pick list
          </MDButton>
        }
      />

      {error && <StateBlock kind="error" title="The orders could not be loaded" message={error} />}
      {loading && !data && <StateBlock kind="loading" title="Loading the orders to ship" />}

      {data && (
        <Box sx={{ display: "grid", gap: 3 }}>
          <Box sx={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: 3 }}>
            <StatCard icon="local_shipping" tone="info" label="To ship" value={orders.length.toLocaleString()} hint={`${orders.reduce((sum, o) => sum + o.units, 0).toLocaleString()} units in all`} />
            <StatCard icon="schedule" tone={late > 0 ? "warning" : "success"} label="Waiting over 2 days" value={late.toLocaleString()} hint="Ship these first" />
            <StatCard icon="production_quantity_limits" tone={short > 0 ? "error" : "success"} label="Short of stock" value={short.toLocaleString()} hint="A line asks for more than is on the shelf" to="/inventory" />
          </Box>

          <Section flush icon="inbox" title="Orders" subtitle="An order stays here until it is shipped or cancelled.">
            <SimpleTable
              rows={orders}
              getRowId={(row: ShippingOrder) => row.id}
              emptyMessage="Nothing is waiting to be shipped."
              columns={[
                {
                  key: "order",
                  header: "Order",
                  render: (row: ShippingOrder) => (
                    <Box sx={{ lineHeight: 1.35 }}>
                      <Box sx={{ fontWeight: 500, color: c.text }}>{row.orderNumber}</Box>
                      <Box sx={{ fontSize: "0.75rem", color: c.muted }} title={formatDateTime(row.createdAtUtc)}>
                        {row.channel} · {timeAgo(row.createdAtUtc)}
                      </Box>
                    </Box>
                  ),
                },
                {
                  key: "items",
                  header: "Items",
                  render: (row: ShippingOrder) => (
                    <Box sx={{ whiteSpace: "normal", maxWidth: 420, fontSize: "0.8125rem", lineHeight: 1.45 }}>
                      {row.lines.map((line) => (
                        <Box key={line.productId} sx={{ color: line.quantity > line.onHand ? c.text : c.muted }}>
                          {line.quantity} × {line.name} <Box component="span" sx={{ fontFamily: "monospace" }}>({line.sku})</Box>
                          {line.quantity > line.onHand && <Box component="span" sx={{ fontWeight: 700 }}> · only {line.onHand} on the shelf</Box>}
                        </Box>
                      ))}
                    </Box>
                  ),
                },
                { key: "status", header: "Status", render: (row: ShippingOrder) => <StatusPill tone={ORDER_STATUS_TONE[row.status]} label={ORDER_STATUS_LABELS[row.status]} /> },
                { key: "total", header: "Total", align: "right", render: (row: ShippingOrder) => formatMoney(row.total) },
                {
                  key: "ship",
                  header: "",
                  align: "right",
                  render: (row: ShippingOrder) => (
                    <MDButton variant="gradient" color="info" size="small" onClick={() => open(row)} aria-label={`Ship ${row.orderNumber}`}>
                      Ship
                    </MDButton>
                  ),
                },
              ]}
            />
          </Section>
        </Box>
      )}

      <KitDialog
        open={!!shipping}
        onClose={() => setShipping(null)}
        onSubmit={ship}
        icon="local_shipping"
        title={shipping ? `Ship ${shipping.orderNumber}` : ""}
        subtitle="The order is completed and leaves this list. The sales channel is not told from here: enter the tracking number there too."
        actions={
          <>
            <MDButton variant="text" color="secondary" onClick={() => setShipping(null)}>
              Cancel
            </MDButton>
            <MDButton type="submit" variant="gradient" color="info" disabled={busy}>
              {busy ? "Saving…" : "Mark as shipped"}
            </MDButton>
          </>
        }
      >
        {shipError && <InlineAlert sx={{ mb: 2.5 }}>{shipError}</InlineAlert>}
        {shipping?.short && (
          <InlineAlert tone="warning" sx={{ mb: 2.5 }}>
            A line of this order asks for more than is on the shelf. Check the parcel really holds everything before marking it shipped.
          </InlineAlert>
        )}
        <Box sx={{ display: "grid", gap: 2.5, pt: 0.5 }}>
          <MDInput select label="Carrier" fullWidth SelectProps={{ native: true }} InputLabelProps={{ shrink: true }} value={carrier} onChange={(e: Change) => setCarrier(e.target.value)}>
            {CARRIERS.map((name) => (
              <option key={name} value={name}>
                {name}
              </option>
            ))}
          </MDInput>
          <MDInput label="Tracking number" fullWidth value={tracking} onChange={(e: Change) => setTracking(e.target.value)} helperText="Optional. Kept with the order and shown under Shipped." />
        </Box>
      </KitDialog>
    </PageShell>
  );
}
