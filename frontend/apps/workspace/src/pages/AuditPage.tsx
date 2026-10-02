import { useEffect, useState } from "react";
import Card from "@mui/material/Card";
import MDBox from "components/MDBox";
import MDTypography from "components/MDTypography";
import DataTable from "examples/Tables/DataTable";
import DashboardLayout from "examples/LayoutContainers/DashboardLayout";
import WorkspaceNavbar from "../components/WorkspaceNavbar";
import Footer from "examples/Footer";
import { useAuth } from "../auth/useAuth";
import { ApiError } from "../lib/api";
import { AuditApi } from "../api/resources";
import type { AuditEntry } from "../api/types";

export default function AuditPage() {
  const { logout } = useAuth();
  const [entries, setEntries] = useState<AuditEntry[] | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    AuditApi.list()
      .then(setEntries)
      .catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load audit log."))
      .finally(() => setLoading(false));
  }, []);

  const columns = [
    { Header: "When", accessor: "when" },
    { Header: "Actor", accessor: "actor" },
    { Header: "Action", accessor: "action" },
    { Header: "Details", accessor: "details" },
  ];

  const rows =
    entries?.map((e) => ({
      when: new Date(e.occurredAtUtc).toLocaleString(),
      actor: e.actorEmail,
      action: e.action,
      details: e.details ?? "",
    })) ?? [];

  return (
    <DashboardLayout>
      <WorkspaceNavbar onLogout={logout} />
      <MDBox py={3}>
        <Card>
          <MDBox p={3}>
            <MDTypography variant="h5">Company audit log</MDTypography>
          </MDBox>
          {loading && (
            <MDBox px={3} pb={3}>
              <MDTypography variant="body2">Loading…</MDTypography>
            </MDBox>
          )}
          {error && (
            <MDBox px={3} pb={3}>
              <MDTypography variant="body2" color="error">
                {error}
              </MDTypography>
            </MDBox>
          )}
          {!loading && !error && entries?.length === 0 && (
            <MDBox px={3} pb={3}>
              <MDTypography variant="body2" color="text">
                No activity recorded yet.
              </MDTypography>
            </MDBox>
          )}
          {!loading && !error && (entries?.length ?? 0) > 0 && <DataTable table={{ columns, rows }} canSearch />}
        </Card>
      </MDBox>
      <Footer />
    </DashboardLayout>
  );
}
