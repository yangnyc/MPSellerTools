import { useEffect, useState } from "react";
import { Link as RouterLink } from "react-router-dom";
import Card from "@mui/material/Card";
import Chip from "@mui/material/Chip";
import Link from "@mui/material/Link";
import MDBox from "components/MDBox";
import MDTypography from "components/MDTypography";
import MDButton from "components/MDButton";
import DataTable from "examples/Tables/DataTable";
import DashboardLayout from "examples/LayoutContainers/DashboardLayout";
import DashboardNavbar from "examples/Navbars/DashboardNavbar";
import Footer from "examples/Footer";
import { useAuth } from "../auth/useAuth";
import { ApiError } from "../lib/api";
import { TenantsApi } from "../api/resources";
import { TENANT_STATUS_LABELS, type TenantStatus, type TenantSummary } from "../api/types";

const STATUS_COLOR: Record<TenantStatus, "info" | "success" | "warning" | "error"> = {
  0: "info",
  1: "success",
  2: "warning",
  3: "error",
};

export default function TenantsListPage() {
  const { logout } = useAuth();
  const [tenants, setTenants] = useState<TenantSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    TenantsApi.list()
      .then(setTenants)
      .catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load companies."))
      .finally(() => setLoading(false));
  }, []);

  const columns = [
    { Header: "Name", accessor: "name" },
    { Header: "Slug", accessor: "slug" },
    { Header: "Status", accessor: "status" },
    { Header: "URL", accessor: "url" },
  ];

  const rows =
    tenants?.map((t) => ({
      name: (
        <Link component={RouterLink} to={`/tenants/${t.id}`}>
          {t.name}
        </Link>
      ),
      slug: t.slug,
      status: <Chip size="small" color={STATUS_COLOR[t.status]} label={TENANT_STATUS_LABELS[t.status]} />,
      url: t.url ? (
        <Link href={t.url} target="_blank" rel="noreferrer">
          {t.url}
        </Link>
      ) : (
        "—"
      ),
    })) ?? [];

  return (
    <DashboardLayout>
      <DashboardNavbar onLogout={logout} />
      <MDBox py={3}>
        <Card>
          <MDBox display="flex" justifyContent="space-between" alignItems="center" p={3}>
            <MDTypography variant="h5">Companies</MDTypography>
            <MDButton component={RouterLink} to="/tenants/new" variant="gradient" color="info">
              Create company
            </MDButton>
          </MDBox>
          {loading && (
            <MDBox p={3}>
              <MDTypography variant="body2">Loading companies…</MDTypography>
            </MDBox>
          )}
          {error && (
            <MDBox p={3}>
              <MDTypography variant="body2" color="error">
                {error}
              </MDTypography>
            </MDBox>
          )}
          {!loading && !error && tenants?.length === 0 && (
            <MDBox p={3}>
              <MDTypography variant="body2" color="text">
                No companies yet.
              </MDTypography>
            </MDBox>
          )}
          {!loading && !error && (tenants?.length ?? 0) > 0 && <DataTable table={{ columns, rows }} canSearch />}
        </Card>
      </MDBox>
      <Footer />
    </DashboardLayout>
  );
}
