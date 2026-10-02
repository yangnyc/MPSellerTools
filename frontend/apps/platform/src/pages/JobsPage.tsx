import { useEffect, useState } from "react";
import Card from "@mui/material/Card";
import Chip from "@mui/material/Chip";
import MDBox from "components/MDBox";
import MDTypography from "components/MDTypography";
import DataTable from "examples/Tables/DataTable";
import DashboardLayout from "examples/LayoutContainers/DashboardLayout";
import PlatformNavbar from "../components/PlatformNavbar";
import Footer from "examples/Footer";
import { useAuth } from "../auth/useAuth";
import { ApiError } from "../lib/api";
import { JobsApi } from "../api/resources";
import { JOB_STATUS_LABELS, JOB_TYPE_LABELS, type ProvisioningJob, type ProvisioningJobStatus } from "../api/types";

const STATUS_COLOR: Record<ProvisioningJobStatus, "info" | "warning" | "success" | "error"> = {
  0: "info",
  1: "warning",
  2: "success",
  3: "error",
};

export default function JobsPage() {
  const { logout } = useAuth();
  const [jobs, setJobs] = useState<ProvisioningJob[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const load = () =>
      JobsApi.list()
        .then(setJobs)
        .catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load jobs."));
    load();
    const timer = setInterval(load, 5000);
    return () => clearInterval(timer);
  }, []);

  const columns = [
    { Header: "Company", accessor: "company" },
    { Header: "Type", accessor: "type" },
    { Header: "Status", accessor: "status" },
    { Header: "Attempts", accessor: "attempts", align: "right" as const },
    { Header: "Error", accessor: "error" },
  ];

  const rows =
    jobs?.map((j) => ({
      company: j.tenantName,
      type: JOB_TYPE_LABELS[j.jobType],
      status: <Chip size="small" color={STATUS_COLOR[j.status]} label={JOB_STATUS_LABELS[j.status]} />,
      attempts: j.attempts,
      error: j.lastError ?? "",
    })) ?? [];

  return (
    <DashboardLayout>
      <PlatformNavbar onLogout={logout} />
      <MDBox py={3}>
        <Card>
          <MDBox p={3}>
            <MDTypography variant="h5">Provisioning jobs</MDTypography>
          </MDBox>
          {error && (
            <MDBox px={3} pb={3}>
              <MDTypography variant="body2" color="error">
                {error}
              </MDTypography>
            </MDBox>
          )}
          {!error && jobs?.length === 0 && (
            <MDBox px={3} pb={3}>
              <MDTypography variant="body2" color="text">
                No provisioning jobs yet.
              </MDTypography>
            </MDBox>
          )}
          {!error && (jobs?.length ?? 0) > 0 && <DataTable table={{ columns, rows }} canSearch />}
        </Card>
      </MDBox>
      <Footer />
    </DashboardLayout>
  );
}
