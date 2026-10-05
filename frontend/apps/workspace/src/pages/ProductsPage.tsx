import { useCallback, useEffect, useMemo, useState } from "react";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import IconButton from "@mui/material/IconButton";
import Tooltip from "@mui/material/Tooltip";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import DataTable from "examples/Tables/DataTable";
import {
  InlineAlert,
  KitDialog,
  PageHeader,
  Section,
  StatCard,
  StateBlock,
  StatusPill,
  formatMoney,
  useKit,
  type KitTone,
} from "examples/Kit";
import PageShell from "../components/PageShell";
import { useAuth } from "../auth/useAuth";
import { useSnackbar } from "../components/useSnackbar";
import ConfirmDialog from "../components/ConfirmDialog";
import { ApiError } from "../lib/api";
import { ProductsApi } from "../api/resources";
import type { Product } from "../api/types";

type ProductFormState = { sku: string; name: string; price: string; stockQuantity: string };
const emptyForm: ProductFormState = { sku: "", name: "", price: "", stockQuantity: "" };

const LOW_STOCK_THRESHOLD = 5;

function stockLevel(quantity: number): { tone: KitTone; label: string } {
  if (quantity === 0) return { tone: "error", label: "Out of stock" };
  if (quantity <= LOW_STOCK_THRESHOLD) return { tone: "warning", label: "Low stock" };
  return { tone: "success", label: "In stock" };
}

export default function ProductsPage() {
  const { user, logout } = useAuth();
  const { notify } = useSnackbar();
  const { c } = useKit();
  const isTenantAdmin = user?.roles.includes("TenantAdmin") ?? false;

  const [products, setProducts] = useState<Product[] | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<Product | null>(null);
  const [form, setForm] = useState<ProductFormState>(emptyForm);
  const [formError, setFormError] = useState<string | null>(null);

  const [archiveTarget, setArchiveTarget] = useState<Product | null>(null);

  const fetchData = useCallback(() => {
    ProductsApi.list()
      .then(setProducts)
      .catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load products."))
      .finally(() => setLoading(false));
  }, []);

  useEffect(fetchData, [fetchData]);

  const load = () => {
    setLoading(true);
    setError(null);
    fetchData();
  };

  const openCreate = () => {
    setEditing(null);
    setForm(emptyForm);
    setFormError(null);
    setFormOpen(true);
  };

  const openEdit = useCallback((product: Product) => {
    setEditing(product);
    setForm({
      sku: product.sku,
      name: product.name,
      price: String(product.price),
      stockQuantity: String(product.stockQuantity),
    });
    setFormError(null);
    setFormOpen(true);
  }, []);

  const submitForm = async () => {
    const price = Number(form.price);
    const stockQuantity = Number(form.stockQuantity);
    if (!form.name.trim()) {
      setFormError("Name is required.");
      return;
    }
    if (!editing && !form.sku.trim()) {
      setFormError("SKU is required.");
      return;
    }
    if (Number.isNaN(price) || price < 0) {
      setFormError("Price must be a non-negative number.");
      return;
    }
    if (!Number.isInteger(stockQuantity) || stockQuantity < 0) {
      setFormError("Stock quantity must be a non-negative whole number.");
      return;
    }

    try {
      if (editing) {
        await ProductsApi.update(editing.id, { name: form.name, price, stockQuantity, rowVersion: editing.rowVersion });
        notify("Product updated.", "success");
      } else {
        await ProductsApi.create({ sku: form.sku, name: form.name, price, stockQuantity });
        notify("Product created.", "success");
      }
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

  const confirmArchive = async () => {
    if (!archiveTarget) return;
    try {
      await ProductsApi.archive(archiveTarget.id);
      notify("Product archived.", "success");
      setArchiveTarget(null);
      load();
    } catch (err) {
      notify(err instanceof ApiError ? err.message : "Archive failed.", "error");
    }
  };

  // Plain values in the rows (so the table's search and sorting work on them)
  // and the presentation in each column's Cell. Memoised because the table
  // resets its page and filter whenever it is handed new row objects.
  const table = useMemo(() => {
    const columns = [
      {
        Header: "SKU",
        accessor: "sku",
        width: "16%",
        Cell: ({ value }: { value: string }) => (
          <Box component="span" sx={{ fontFamily: "monospace", fontSize: "0.8125rem", color: c.muted }}>
            {value}
          </Box>
        ),
      },
      {
        Header: "Name",
        accessor: "name",
        Cell: ({ value }: { value: string }) => (
          <Box component="span" sx={{ fontWeight: 500, color: c.text }}>
            {value}
          </Box>
        ),
      },
      {
        Header: "Price",
        accessor: "price",
        align: "right" as const,
        Cell: ({ value }: { value: number }) => formatMoney(value),
      },
      {
        Header: "Stock",
        accessor: "stockQuantity",
        align: "right" as const,
        Cell: ({ value }: { value: number }) => {
          const level = stockLevel(value);
          return (
            <Box sx={{ display: "inline-flex", alignItems: "center", gap: 1.25 }}>
              <Box component="span" sx={{ fontWeight: 500, color: c.text }}>
                {value}
              </Box>
              <StatusPill tone={level.tone} label={level.label} />
            </Box>
          );
        },
      },
      ...(isTenantAdmin
        ? [
            {
              Header: "Actions",
              accessor: "id",
              align: "right" as const,
              disableSortBy: true,
              disableGlobalFilter: true,
              Cell: ({ row }: { row: { original: Product } }) => (
                <Box sx={{ display: "flex", justifyContent: "flex-end", gap: 0.5 }}>
                  <Tooltip title="Edit">
                    <IconButton
                      size="small"
                      onClick={() => openEdit(row.original)}
                      aria-label={`Edit ${row.original.name}`}
                      sx={{ color: c.muted }}
                    >
                      <Icon fontSize="small">edit</Icon>
                    </IconButton>
                  </Tooltip>
                  <Tooltip title="Archive">
                    <IconButton
                      size="small"
                      onClick={() => setArchiveTarget(row.original)}
                      aria-label={`Archive ${row.original.name}`}
                      sx={{ color: c.muted }}
                    >
                      <Icon fontSize="small">archive</Icon>
                    </IconButton>
                  </Tooltip>
                </Box>
              ),
            },
          ]
        : []),
    ];
    return { columns, rows: products ?? [] };
  }, [products, isTenantAdmin, c, openEdit]);

  const totalUnits = products?.reduce((sum, p) => sum + p.stockQuantity, 0);
  const inventoryValue = products?.reduce((sum, p) => sum + p.price * p.stockQuantity, 0);
  const needRestock = products?.filter((p) => p.stockQuantity <= LOW_STOCK_THRESHOLD).length;

  const setField = (field: keyof ProductFormState) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setForm({ ...form, [field]: e.target.value });

  return (
    <PageShell>
      <PageHeader
        icon="inventory_2"
        title="Products"
        subtitle={
          isTenantAdmin ? "Your catalog, prices, and stock levels." : "The company catalog, for reference."
        }
        actions={
          isTenantAdmin && (
            <MDButton variant="gradient" color="info" onClick={openCreate} startIcon={<Icon>add</Icon>}>
              Add product
            </MDButton>
          )
        }
      />

      <Box sx={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: 3, mb: 3 }}>
        <StatCard icon="category" tone="primary" label="In catalog" value={products?.length} hint="Active listings" />
        <StatCard icon="warehouse" tone="info" label="Units in stock" value={totalUnits} hint="Across the catalog" />
        <StatCard
          icon="payments"
          tone="success"
          label="Inventory value"
          value={inventoryValue === undefined ? undefined : formatMoney(inventoryValue)}
          hint="Price × stock"
        />
        <StatCard
          icon="production_quantity_limits"
          tone="warning"
          label="Need restocking"
          value={needRestock}
          hint={`${LOW_STOCK_THRESHOLD} units or fewer`}
        />
      </Box>

      {isTenantAdmin && (
        <InlineAlert tone="info" sx={{ mb: 3 }}>
          Stock is edited by hand. Creating or completing an order does not change these quantities.
        </InlineAlert>
      )}

      <Section flush>
        {loading && <StateBlock kind="loading" title="Loading products" />}
        {error && (
          <StateBlock
            kind="error"
            title="Products could not be loaded"
            message={error}
            action={
              <MDButton variant="outlined" color="info" size="small" onClick={load}>
                Try again
              </MDButton>
            }
          />
        )}
        {!loading && !error && products?.length === 0 && (
          <StateBlock
            icon="inventory_2"
            title="No products yet"
            message={isTenantAdmin ? "Add your first product to start building the catalog." : "The catalog is empty."}
            action={
              isTenantAdmin && (
                <MDButton variant="gradient" color="info" size="small" onClick={openCreate}>
                  Add product
                </MDButton>
              )
            }
          />
        )}
        {!loading && !error && (products?.length ?? 0) > 0 && <DataTable table={table} canSearch />}
      </Section>

      <KitDialog
        open={formOpen}
        onClose={() => setFormOpen(false)}
        onSubmit={submitForm}
        icon={editing ? "edit" : "add"}
        title={editing ? "Edit product" : "Add product"}
        subtitle={editing ? "The SKU cannot be changed after creation." : "The SKU must be unique in your catalog."}
        actions={
          <>
            <MDButton variant="text" color="secondary" onClick={() => setFormOpen(false)}>
              Cancel
            </MDButton>
            <MDButton type="submit" variant="gradient" color="info">
              Save
            </MDButton>
          </>
        }
      >
        {formError && <InlineAlert sx={{ mb: 2.5 }}>{formError}</InlineAlert>}
        <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", sm: "1fr 1fr" }, gap: 2.5, pt: 0.5 }}>
          <MDInput label="SKU" fullWidth disabled={!!editing} value={form.sku} onChange={setField("sku")} />
          <MDInput label="Name" fullWidth value={form.name} onChange={setField("name")} />
          <MDInput
            label="Price"
            type="number"
            fullWidth
            inputProps={{ min: 0, step: "0.01" }}
            value={form.price}
            onChange={setField("price")}
          />
          <MDInput
            label="Stock quantity"
            type="number"
            fullWidth
            inputProps={{ min: 0, step: 1 }}
            value={form.stockQuantity}
            onChange={setField("stockQuantity")}
          />
        </Box>
      </KitDialog>

      <ConfirmDialog
        open={!!archiveTarget}
        title="Archive product"
        message={`Archive "${archiveTarget?.name}"? It will no longer appear in the catalog, but past orders referencing it are kept.`}
        confirmLabel="Archive"
        onConfirm={confirmArchive}
        onCancel={() => setArchiveTarget(null)}
      />
    </PageShell>
  );
}
