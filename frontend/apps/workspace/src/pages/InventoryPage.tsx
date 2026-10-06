import { useCallback, useEffect, useMemo, useState } from "react";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import DataTable from "examples/Tables/DataTable";
import {
  FilterTabs,
  Identity,
  InlineAlert,
  KitDialog,
  PageHeader,
  Section,
  StatCard,
  StateBlock,
  StatusPill,
  downloadCsv,
  formatDateTime,
  timeAgo,
  useKit,
  type KitTone,
} from "examples/Kit";
import PageShell from "../components/PageShell";
import { useSnackbar } from "../components/useSnackbar";
import { ApiError } from "../lib/api";
import {
  InventoryApi,
  type InventoryItem,
  type InventoryMovement,
  type InventoryMovementType,
  type InventoryOverview,
} from "../api/channels";

const MOVEMENT: Record<InventoryMovementType, { label: string; tone: KitTone }> = {
  0: { label: "Counted", tone: "info" },
  1: { label: "Held for an order", tone: "warning" },
  2: { label: "Hold released", tone: "neutral" },
  3: { label: "Shipped", tone: "success" },
  4: { label: "Return received", tone: "primary" },
};

type View = "stock" | "ledger";
type Change = React.ChangeEvent<HTMLInputElement>;

const message = (err: unknown, fallback: string) => (err instanceof ApiError ? err.message : fallback);
const signed = (n: number) => (n > 0 ? `+${n}` : String(n));
const wholeNumber = (text: string) => text !== "" && Number.isInteger(Number(text)) && Number(text) >= 0;

// The merchant warehouse: what is on hand, what is held for orders, and every change to either.
export default function InventoryPage() {
  const { c } = useKit();
  const { notify } = useSnackbar();

  const [overview, setOverview] = useState<InventoryOverview | null>(null);
  const [movements, setMovements] = useState<InventoryMovement[] | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [view, setView] = useState<View>("stock");
  const [saving, setSaving] = useState(false);

  const [counting, setCounting] = useState<InventoryItem | null>(null);
  const [onHand, setOnHand] = useState("");
  const [safetyStock, setSafetyStock] = useState("");

  const [returning, setReturning] = useState<InventoryItem | null>(null);
  const [returnQuantity, setReturnQuantity] = useState("");
  const [receiptId, setReceiptId] = useState("");
  const [formError, setFormError] = useState<string | null>(null);

  const load = useCallback(
    () =>
      Promise.all([InventoryApi.overview(), InventoryApi.movements()])
        .then(([nextOverview, nextMovements]) => {
          setOverview(nextOverview);
          setMovements(nextMovements);
          setLoadError(null);
        })
        .catch((err) => setLoadError(message(err, "Failed to load inventory."))),
    []
  );

  useEffect(() => {
    load();
  }, [load]);

  const openCount = useCallback((item: InventoryItem) => {
    setCounting(item);
    setOnHand(String(item.onHand));
    setSafetyStock(String(item.safetyStock));
    setFormError(null);
  }, []);

  const openReturn = useCallback((item: InventoryItem) => {
    setReturning(item);
    setReturnQuantity("1");
    setReceiptId("");
    setFormError(null);
  }, []);

  const submit = async (action: () => Promise<string>) => {
    setSaving(true);
    try {
      notify(await action(), "success");
      setCounting(null);
      setReturning(null);
      await load();
    } catch (err) {
      setFormError(message(err, "Save failed."));
    } finally {
      setSaving(false);
    }
  };

  const saveCount = () => {
    if (!counting) return;
    if (!wholeNumber(onHand) || !wholeNumber(safetyStock)) {
      setFormError("On hand and safety stock are whole numbers, 0 or more.");
      return;
    }
    submit(async () => {
      await InventoryApi.adjust(counting.variantId, Number(onHand), Number(safetyStock));
      return `Stock of ${counting.sku} updated.`;
    });
  };

  const saveReturn = () => {
    if (!returning) return;
    if (!wholeNumber(returnQuantity) || Number(returnQuantity) === 0 || !receiptId.trim()) {
      setFormError("A return needs a quantity of 1 or more and your reference for the receipt.");
      return;
    }
    submit(async () => {
      const { recorded } = await InventoryApi.receiveReturn(returning.variantId, Number(returnQuantity), receiptId.trim());
      return recorded ? `${returnQuantity} of ${returning.sku} put back on hand.` : "This receipt was already recorded; nothing changed.";
    });
  };

  const items = overview?.items;

  const stockTable = useMemo(() => {
    type CellProps = { row: { original: InventoryItem } };
    const number = (value: number, strong = false) => (
      <Box component="span" sx={{ fontWeight: strong ? 700 : 400, color: strong ? c.text : c.muted }}>
        {value}
      </Box>
    );
    const columns = [
      {
        Header: "Product",
        id: "product",
        accessor: (item: InventoryItem) => `${item.productName} ${item.variantName ?? ""} ${item.sku}`,
        Cell: ({ row }: CellProps) => (
          <Identity
            name={row.original.variantName ? `${row.original.productName} · ${row.original.variantName}` : row.original.productName}
            secondary={row.original.sku}
            square
          />
        ),
      },
      { Header: "On hand", accessor: "onHand", align: "right" as const, Cell: ({ value }: { value: number }) => number(value) },
      { Header: "Held", accessor: "reserved", align: "right" as const, Cell: ({ value }: { value: number }) => number(value) },
      { Header: "Safety stock", accessor: "safetyStock", align: "right" as const, Cell: ({ value }: { value: number }) => number(value) },
      {
        Header: "Left to sell",
        accessor: "availableToSell",
        align: "right" as const,
        Cell: ({ value }: { value: number }) => (
          <Box sx={{ display: "inline-flex", alignItems: "center", gap: 1.25 }}>
            {number(value, true)}
            {value === 0 && <StatusPill tone="error" label="None" />}
          </Box>
        ),
      },
      {
        Header: "",
        id: "actions",
        accessor: "variantId",
        align: "right" as const,
        disableSortBy: true,
        disableGlobalFilter: true,
        Cell: ({ row }: CellProps) => (
          <Box sx={{ display: "flex", justifyContent: "flex-end", gap: 1 }}>
            <MDButton variant="outlined" color="info" size="small" onClick={() => openCount(row.original)} aria-label={`Count ${row.original.sku}`}>
              Count
            </MDButton>
            <MDButton variant="text" color="info" size="small" onClick={() => openReturn(row.original)} aria-label={`Receive a return of ${row.original.sku}`}>
              Return
            </MDButton>
          </Box>
        ),
      },
    ];
    return { columns, rows: items ?? [] };
  }, [items, c, openCount, openReturn]);

  const ledgerTable = useMemo(() => {
    type CellProps = { row: { original: InventoryMovement } };
    const columns = [
      {
        Header: "When",
        accessor: "occurredAtUtc",
        Cell: ({ value }: { value: string }) => <Box title={formatDateTime(value)}>{timeAgo(value)}</Box>,
      },
      { Header: "SKU", accessor: "sku" },
      {
        Header: "What",
        id: "type",
        accessor: (movement: InventoryMovement) => MOVEMENT[movement.type].label,
        Cell: ({ row }: CellProps) => <StatusPill {...MOVEMENT[row.original.type]} />,
      },
      { Header: "On hand", accessor: "onHandDelta", align: "right" as const, Cell: ({ value }: { value: number }) => (value === 0 ? "—" : signed(value)) },
      { Header: "Held", accessor: "reservedDelta", align: "right" as const, Cell: ({ value }: { value: number }) => (value === 0 ? "—" : signed(value)) },
      {
        Header: "Reference",
        id: "reference",
        accessor: (movement: InventoryMovement) => movement.reference ?? "",
        Cell: ({ value }: { value: string }) => <Box sx={{ maxWidth: 320, whiteSpace: "normal", overflowWrap: "anywhere" }}>{value || "—"}</Box>,
      },
    ];
    return { columns, rows: movements ?? [] };
  }, [movements]);

  const exportCsv = () =>
    downloadCsv(
      "inventory.csv",
      ["SKU", "Product", "Variant", "On hand", "Held", "Safety stock", "Left to sell"],
      (items ?? []).map((i) => [i.sku, i.productName, i.variantName, i.onHand, i.reserved, i.safetyStock, i.availableToSell])
    );

  const loading = !loadError && (!overview || !movements);
  const total = (pick: (item: InventoryItem) => number) => items?.reduce((sum, item) => sum + pick(item), 0);

  return (
    <PageShell>
      <PageHeader
        icon="warehouse"
        title="Inventory"
        subtitle="What is in your warehouse, what is held for orders, and what is left to sell."
        actions={
          <MDButton variant="outlined" color="info" disabled={!items?.length} onClick={exportCsv} startIcon={<Icon>file_download</Icon>}>
            Export CSV
          </MDButton>
        }
      />

      {loadError && <StateBlock kind="error" title="Inventory could not be loaded" message={loadError} />}
      {loading && <StateBlock kind="loading" title="Loading inventory" />}

      {!loading && !loadError && overview && (
        <Box sx={{ display: "grid", gap: 3 }}>
          <Box sx={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: 3 }}>
            <StatCard icon="warehouse" tone="info" label="On hand" value={total((i) => i.onHand)} hint="Units in the warehouse" />
            <StatCard icon="lock_clock" tone="warning" label="Held for orders" value={total((i) => i.reserved)} hint="Sold but not yet shipped" />
            <StatCard icon="sell" tone="success" label="Left to sell" value={total((i) => i.availableToSell)} hint="On hand − held − safety stock" />
            <StatCard icon="remove_shopping_cart" tone="error" label="Nothing left to sell" value={items!.filter((i) => i.availableToSell === 0).length} hint="Products and variants" />
          </Box>

          {!overview.accountingEnabled && (
            <InlineAlert tone="info" title="Orders do not change stock yet">
              Stock accounting is switched off for this workspace, so orders neither hold nor deduct units and quantities are edited by hand. It
              is switched on by the host setting Marketplace:InventoryAccountingEnabled.
            </InlineAlert>
          )}

          <Section
            title={view === "stock" ? "Stock" : "Stock ledger"}
            subtitle={view === "stock" ? "One row per product or variant." : "The newest 100 changes: counts, holds, shipments and returns."}
            actions={
              <FilterTabs
                label="View"
                value={view}
                onChange={setView}
                options={[
                  { value: "stock", label: "Stock", count: items!.length },
                  { value: "ledger", label: "Ledger", count: movements!.length },
                ]}
              />
            }
            flush
          >
            {view === "stock" &&
              (items!.length === 0 ? (
                <StateBlock icon="warehouse" title="No products yet" message="Products you add to the catalog show up here with their stock." />
              ) : (
                <DataTable table={stockTable} canSearch />
              ))}
            {view === "ledger" &&
              (movements!.length === 0 ? (
                <StateBlock icon="history" title="No stock changes yet" message="Counts, holds, shipments and returns are recorded here." />
              ) : (
                <DataTable table={ledgerTable} canSearch />
              ))}
          </Section>
        </Box>
      )}

      <KitDialog
        open={!!counting}
        onClose={() => setCounting(null)}
        onSubmit={saveCount}
        icon="fact_check"
        title={`Count ${counting?.sku ?? ""}`}
        subtitle="Enter what is physically there. Units held for orders are not affected."
        actions={
          <>
            <MDButton variant="text" color="secondary" onClick={() => setCounting(null)}>
              Cancel
            </MDButton>
            <MDButton type="submit" variant="gradient" color="info" disabled={saving}>
              {saving ? "Saving…" : "Save"}
            </MDButton>
          </>
        }
      >
        {formError && <InlineAlert sx={{ mb: 2.5 }}>{formError}</InlineAlert>}
        <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", sm: "1fr 1fr" }, gap: 2.5, pt: 0.5 }}>
          <MDInput label="On hand" type="number" fullWidth inputProps={{ min: 0, step: 1 }} value={onHand} onChange={(e: Change) => setOnHand(e.target.value)} />
          <MDInput
            label="Safety stock"
            type="number"
            fullWidth
            inputProps={{ min: 0, step: 1 }}
            value={safetyStock}
            onChange={(e: Change) => setSafetyStock(e.target.value)}
            helperText="Kept back and never offered for sale."
          />
        </Box>
      </KitDialog>

      <KitDialog
        open={!!returning}
        onClose={() => setReturning(null)}
        onSubmit={saveReturn}
        icon="assignment_return"
        title={`Receive a return of ${returning?.sku ?? ""}`}
        subtitle="For goods that have arrived back at the warehouse. They go back on hand."
        actions={
          <>
            <MDButton variant="text" color="secondary" onClick={() => setReturning(null)}>
              Cancel
            </MDButton>
            <MDButton type="submit" variant="gradient" color="info" disabled={saving}>
              {saving ? "Saving…" : "Receive"}
            </MDButton>
          </>
        }
      >
        {formError && <InlineAlert sx={{ mb: 2.5 }}>{formError}</InlineAlert>}
        <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", sm: "1fr 2fr" }, gap: 2.5, pt: 0.5 }}>
          <MDInput label="Quantity" type="number" fullWidth inputProps={{ min: 1, step: 1 }} value={returnQuantity} onChange={(e: Change) => setReturnQuantity(e.target.value)} />
          <MDInput
            label="Receipt reference"
            fullWidth
            value={receiptId}
            onChange={(e: Change) => setReceiptId(e.target.value)}
            helperText="Your own reference. The same one is only ever counted once."
          />
        </Box>
      </KitDialog>
    </PageShell>
  );
}
