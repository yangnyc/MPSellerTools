import { useCallback, useEffect, useState } from "react";
import { useParams } from "react-router-dom";
import Card from "@mui/material/Card";
import Chip from "@mui/material/Chip";
import Icon from "@mui/material/Icon";
import IconButton from "@mui/material/IconButton";
import Link from "@mui/material/Link";
import MDBox from "components/MDBox";
import MDTypography from "components/MDTypography";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import DashboardLayout from "examples/LayoutContainers/DashboardLayout";
import PlatformNavbar from "../components/PlatformNavbar";
import Footer from "examples/Footer";
import { useAuth } from "../auth/useAuth";
import { useSnackbar } from "../components/useSnackbar";
import ConfirmDialog from "../components/ConfirmDialog";
import { ApiError } from "../lib/api";
import { TenantsApi } from "../api/resources";
import { TENANT_STATUS_LABELS, type TenantDetail, type TenantStatus } from "../api/types";

const STATUS_COLOR: Record<TenantStatus, "info" | "success" | "warning" | "error"> = {
  0: "info",
  1: "success",
  2: "warning",
  3: "error",
};

export default function TenantDetailPage() {
  const { id } = useParams<{ id: string }>();
  const { logout } = useAuth();
  const { notify } = useSnackbar();

  const [tenant, setTenant] = useState<TenantDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [confirmAction, setConfirmAction] = useState<"suspend" | "resume" | "retry" | null>(null);

  const [editingName, setEditingName] = useState(false);
  const [nameDraft, setNameDraft] = useState("");
  const [savingName, setSavingName] = useState(false);

  const load = useCallback(() => {
    if (!id) return;
    TenantsApi.get(id)
      .then((value) => { setTenant(value); setError(null); })
      .catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load company."));
  }, [id]);

  useEffect(load, [load]);
  // Poll while provisioning so the page updates itself once the worker finishes.
  useEffect(() => {
    if (tenant?.status !== 0) return;
    const timer = setInterval(load, 3000);
    return () => clearInterval(timer);
  }, [tenant?.status, load]);

  const startEditingName = () => {
    setNameDraft(tenant?.name ?? "");
    setEditingName(true);
  };

  const cancelEditingName = () => {
    setEditingName(false);
    setNameDraft(tenant?.name ?? "");
  };

  const saveName = async () => {
    if (!id) return;
    const trimmed = nameDraft.trim();
    if (!trimmed) {
      notify("Name can't be empty.", "error");
      return;
    }
    setSavingName(true);
    try {
      const updated = await TenantsApi.update(id, { name: trimmed });
      setTenant(updated);
      notify("Company renamed.", "success");
      setEditingName(false);
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        await logout();
        return;
      }
      notify(err instanceof ApiError ? err.message : "Failed to rename company.", "error");
    } finally {
      setSavingName(false);
    }
  };

  const runAction = async () => {
    if (!id || !confirmAction) return;
    try {
      if (confirmAction === "suspend") await TenantsApi.suspend(id);
      if (confirmAction === "resume") await TenantsApi.resume(id);
      if (confirmAction === "retry") await TenantsApi.retryProvisioning(id);
      notify("Action started.", "success");
      setConfirmAction(null);
      load();
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        await logout();
        return;
      }
      notify(err instanceof ApiError ? err.message : "Action failed.", "error");
      setConfirmAction(null);
    }
  };

  if (error) {
    return (
      <DashboardLayout>
        <PlatformNavbar onLogout={logout} />
        <MDBox py={3}>
          <MDTypography color="error">{error}</MDTypography>
        </MDBox>
        <Footer />
      </DashboardLayout>
    );
  }

  if (!tenant) {
    return (
      <DashboardLayout>
        <PlatformNavbar onLogout={logout} />
        <MDBox py={3}>
          <MDTypography variant="body2">Loading…</MDTypography>
        </MDBox>
        <Footer />
      </DashboardLayout>
    );
  }

  return (
    <DashboardLayout>
      <PlatformNavbar onLogout={logout} />
      <MDBox py={3}>
        <Card sx={{ maxWidth: 640 }}>
          <MDBox p={3}>
            <MDBox display="flex" justifyContent="space-between" alignItems="center" mb={2}>
              {editingName ? (
                <MDBox display="flex" alignItems="center" gap={1}>
                  <MDInput
                    size="small"
                    label="Company name"
                    autoFocus
                    value={nameDraft}
                    onChange={(e: React.ChangeEvent<HTMLInputElement>) => setNameDraft(e.target.value)}
                    onKeyDown={(e: React.KeyboardEvent) => {
                      if (e.key === "Enter") saveName();
                      if (e.key === "Escape") cancelEditingName();
                    }}
                  />
                  <IconButton size="small" color="info" aria-label="Save name" disabled={savingName} onClick={saveName}>
                    <Icon>check</Icon>
                  </IconButton>
                  <IconButton size="small" aria-label="Cancel" disabled={savingName} onClick={cancelEditingName}>
                    <Icon>close</Icon>
                  </IconButton>
                </MDBox>
              ) : (
                <MDBox display="flex" alignItems="center" gap={1}>
                  <MDTypography variant="h5">{tenant.name}</MDTypography>
                  <IconButton size="small" aria-label="Edit name" onClick={startEditingName}>
                    <Icon fontSize="small">edit</Icon>
                  </IconButton>
                </MDBox>
              )}
              <Chip color={STATUS_COLOR[tenant.status]} label={TENANT_STATUS_LABELS[tenant.status]} />
            </MDBox>
            <MDTypography variant="body2" color="text">
              Slug: {tenant.slug}
            </MDTypography>
            <MDTypography variant="body2" color="text" mb={1}>
              Login URL:{" "}
              {tenant.url ? (
                <Link href={tenant.url} target="_blank" rel="noreferrer">
                  {tenant.url}
                </Link>
              ) : (
                "not yet assigned"
              )}
            </MDTypography>
            {tenant.status === 0 && (
              <MDTypography variant="body2" color="info">
                Provisioning in progress — this page updates automatically.
              </MDTypography>
            )}
            {tenant.failureReason && (
              <MDTypography variant="body2" color="error">
                Failure: {tenant.failureReason}
              </MDTypography>
            )}
            <MDBox mt={3} display="flex" gap={2}>
              {tenant.status === 1 && (
                <MDButton variant="gradient" color="warning" onClick={() => setConfirmAction("suspend")}>
                  Suspend
                </MDButton>
              )}
              {tenant.status === 2 && (
                <MDButton variant="gradient" color="success" onClick={() => setConfirmAction("resume")}>
                  Resume
                </MDButton>
              )}
              {tenant.status === 3 && (
                <MDButton variant="gradient" color="info" onClick={() => setConfirmAction("retry")}>
                  Retry provisioning
                </MDButton>
              )}
            </MDBox>
          </MDBox>
        </Card>
      </MDBox>
      <Footer />

      <ConfirmDialog
        open={!!confirmAction}
        title={
          confirmAction === "suspend" ? "Suspend company" : confirmAction === "resume" ? "Resume company" : "Retry provisioning"
        }
        message={`Are you sure you want to ${confirmAction} "${tenant.name}"?`}
        confirmLabel={confirmAction === "suspend" ? "Suspend" : confirmAction === "resume" ? "Resume" : "Retry"}
        confirmColor={confirmAction === "suspend" ? "warning" : "info"}
        onConfirm={runAction}
        onCancel={() => setConfirmAction(null)}
      />
    </DashboardLayout>
  );
}
