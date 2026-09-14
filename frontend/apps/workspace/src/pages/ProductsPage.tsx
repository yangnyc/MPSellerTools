import { useEffect, useState } from "react";
import Card from "@mui/material/Card";
import Dialog from "@mui/material/Dialog";
import DialogTitle from "@mui/material/DialogTitle";
import DialogContent from "@mui/material/DialogContent";
import DialogActions from "@mui/material/DialogActions";
import Icon from "@mui/material/Icon";
import IconButton from "@mui/material/IconButton";
import MDBox from "components/MDBox";
import MDTypography from "components/MDTypography";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import DataTable from "examples/Tables/DataTable";
import DashboardLayout from "examples/LayoutContainers/DashboardLayout";
import DashboardNavbar from "examples/Navbars/DashboardNavbar";
import Footer from "examples/Footer";
import { useAuth } from "../auth/useAuth";
import { useSnackbar } from "../components/useSnackbar";
import ConfirmDialog from "../components/ConfirmDialog";
import { ApiError } from "../lib/api";
import { ProductsApi } from "../api/resources";
import type { Product } from "../api/types";

type ProductFormState = { sku: string; name: string; price: string; stockQuantity: string };
const emptyForm: ProductFormState = { sku: "", name: "", price: "", stockQuantity: "" };

export default function ProductsPage() {
  const { user, logout } = useAuth();
  const { notify } = useSnackbar();
  const isTenantAdmin = user?.roles.includes("TenantAdmin") ?? false;

  const [products, setProducts] = useState<Product[] | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<Product | null>(null);
  const [form, setForm] = useState<ProductFormState>(emptyForm);
  const [formError, setFormError] = useState<string | null>(null);

  const [archiveTarget, setArchiveTarget] = useState<Product | null>(null);

  const load = () => {
    setLoading(true);
    setError(null);
    ProductsApi.list()
      .then(setProducts)
      .catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load products."))
      .finally(() => setLoading(false));
  };

  useEffect(load, []);

  const openCreate = () => {
    setEditing(null);
    setForm(emptyForm);
    setFormError(null);
    setFormOpen(true);
  };

  const openEdit = (product: Product) => {
    setEditing(product);
    setForm({
      sku: product.sku,
      name: product.name,
      price: String(product.price),
      stockQuantity: String(product.stockQuantity),
    });
    setFormError(null);
    setFormOpen(true);
  };

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

  const columns = [
    { Header: "SKU", accessor: "sku", width: "15%" },
    { Header: "Name", accessor: "name" },
    { Header: "Price", accessor: "price", align: "right" as const },
    { Header: "Stock", accessor: "stock", align: "right" as const },
    ...(isTenantAdmin ? [{ Header: "Actions", accessor: "actions", align: "right" as const }] : []),
  ];

  const rows =
    products?.map((p) => ({
      sku: p.sku,
      name: p.name,
      price: `$${p.price.toFixed(2)}`,
      stock: p.stockQuantity,
      actions: isTenantAdmin ? (
        <MDBox display="flex" justifyContent="flex-end" gap={1}>
          <IconButton size="small" onClick={() => openEdit(p)} aria-label={`Edit ${p.name}`}>
            <Icon fontSize="small">edit</Icon>
          </IconButton>
          <IconButton size="small" onClick={() => setArchiveTarget(p)} aria-label={`Archive ${p.name}`}>
            <Icon fontSize="small">archive</Icon>
          </IconButton>
        </MDBox>
      ) : null,
    })) ?? [];

  return (
    <DashboardLayout>
      <DashboardNavbar onLogout={logout} />
      <MDBox py={3}>
        <Card>
          <MDBox display="flex" justifyContent="space-between" alignItems="center" p={3}>
            <MDTypography variant="h5">Products</MDTypography>
            {isTenantAdmin && (
              <MDButton variant="gradient" color="info" onClick={openCreate}>
                Add product
              </MDButton>
            )}
          </MDBox>
          {loading && (
            <MDBox p={3}>
              <MDTypography variant="body2">Loading products…</MDTypography>
            </MDBox>
          )}
          {error && (
            <MDBox p={3}>
              <MDTypography variant="body2" color="error">
                {error}
              </MDTypography>
            </MDBox>
          )}
          {!loading && !error && products?.length === 0 && (
            <MDBox p={3}>
              <MDTypography variant="body2" color="text">
                No products yet.
              </MDTypography>
            </MDBox>
          )}
          {!loading && !error && (products?.length ?? 0) > 0 && (
            <DataTable table={{ columns, rows }} canSearch />
          )}
        </Card>
      </MDBox>
      <Footer />

      <Dialog open={formOpen} onClose={() => setFormOpen(false)} fullWidth maxWidth="sm">
        <DialogTitle>{editing ? "Edit product" : "Add product"}</DialogTitle>
        <DialogContent>
          {formError && (
            <MDBox mb={2}>
              <MDTypography variant="caption" color="error">
                {formError}
              </MDTypography>
            </MDBox>
          )}
          <MDBox mb={2} mt={1}>
            <MDInput
              label="SKU"
              fullWidth
              disabled={!!editing}
              value={form.sku}
              onChange={(e: React.ChangeEvent<HTMLInputElement>) => setForm({ ...form, sku: e.target.value })}
            />
          </MDBox>
          <MDBox mb={2}>
            <MDInput
              label="Name"
              fullWidth
              value={form.name}
              onChange={(e: React.ChangeEvent<HTMLInputElement>) => setForm({ ...form, name: e.target.value })}
            />
          </MDBox>
          <MDBox mb={2}>
            <MDInput
              label="Price"
              type="number"
              fullWidth
              value={form.price}
              onChange={(e: React.ChangeEvent<HTMLInputElement>) => setForm({ ...form, price: e.target.value })}
            />
          </MDBox>
          <MDBox mb={1}>
            <MDInput
              label="Stock quantity"
              type="number"
              fullWidth
              value={form.stockQuantity}
              onChange={(e: React.ChangeEvent<HTMLInputElement>) => setForm({ ...form, stockQuantity: e.target.value })}
            />
          </MDBox>
        </DialogContent>
        <DialogActions>
          <MDButton variant="text" color="secondary" onClick={() => setFormOpen(false)}>
            Cancel
          </MDButton>
          <MDButton variant="gradient" color="info" onClick={submitForm}>
            Save
          </MDButton>
        </DialogActions>
      </Dialog>

      <ConfirmDialog
        open={!!archiveTarget}
        title="Archive product"
        message={`Archive "${archiveTarget?.name}"? It will no longer appear in the catalog, but past orders referencing it are kept.`}
        confirmLabel="Archive"
        onConfirm={confirmArchive}
        onCancel={() => setArchiveTarget(null)}
      />
    </DashboardLayout>
  );
}
