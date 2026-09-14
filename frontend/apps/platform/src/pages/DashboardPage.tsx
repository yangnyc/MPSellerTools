import { useEffect, useState } from "react";
import Grid from "@mui/material/Grid";
import Chip from "@mui/material/Chip";
import MDBox from "components/MDBox";
import MDTypography from "components/MDTypography";
import ComplexStatisticsCard from "examples/Cards/StatisticsCards/ComplexStatisticsCard";
import DashboardLayout from "examples/LayoutContainers/DashboardLayout";
import DashboardNavbar from "examples/Navbars/DashboardNavbar";
import Footer from "examples/Footer";
import { useAuth } from "../auth/useAuth";
import { DashboardApi } from "../api/resources";
import { JOB_STATUS_LABELS, JOB_TYPE_LABELS, type PlatformDashboard } from "../api/types";

export default function DashboardPage() {
  const { logout } = useAuth();
  const [data, setData] = useState<PlatformDashboard | null>(null);

  useEffect(() => {
    DashboardApi.get()
      .then(setData)
      .catch(() => setData(null));
  }, []);

  return (
    <DashboardLayout>
      <DashboardNavbar onLogout={logout} />
      <MDBox py={3}>
        <Grid container spacing={3}>
          <Grid item xs={12}>
            <MDTypography variant="h4" fontWeight="medium">
              Platform Dashboard
            </MDTypography>
            <MDTypography variant="body2" color="text" mb={2}>
              Tenant counts, instance states, and recent provisioning jobs.
            </MDTypography>
          </Grid>
          {data && (
            <>
              <Grid item xs={12} md={6} lg={3}>
                <ComplexStatisticsCard
                  color="success"
                  icon="check_circle"
                  title="Active"
                  count={data.tenantCounts.active}
                  percentage={{ color: "success", amount: "", label: "companies running" }}
                />
              </Grid>
              <Grid item xs={12} md={6} lg={3}>
                <ComplexStatisticsCard
                  color="info"
                  icon="hourglass_top"
                  title="Provisioning"
                  count={data.tenantCounts.provisioning}
                  percentage={{ color: "info", amount: "", label: "in progress" }}
                />
              </Grid>
              <Grid item xs={12} md={6} lg={3}>
                <ComplexStatisticsCard
                  color="warning"
                  icon="pause_circle"
                  title="Suspended"
                  count={data.tenantCounts.suspended}
                  percentage={{ color: "warning", amount: "", label: "companies" }}
                />
              </Grid>
              <Grid item xs={12} md={6} lg={3}>
                <ComplexStatisticsCard
                  color="error"
                  icon="error"
                  title="Failed"
                  count={data.tenantCounts.failed}
                  percentage={{ color: "error", amount: "", label: "need attention" }}
                />
              </Grid>
              <Grid item xs={12}>
                <MDTypography variant="h6" mt={2} mb={1}>
                  Recent provisioning jobs
                </MDTypography>
                {data.recentJobs.length === 0 && (
                  <MDTypography variant="body2" color="text">
                    No provisioning activity yet.
                  </MDTypography>
                )}
                {data.recentJobs.map((job) => (
                  <MDBox key={job.id} display="flex" alignItems="center" gap={1} mb={1}>
                    <Chip size="small" label={JOB_TYPE_LABELS[job.jobType]} />
                    <MDTypography variant="body2">{job.tenantName}</MDTypography>
                    <MDTypography variant="caption" color="text">
                      {JOB_STATUS_LABELS[job.status]}
                    </MDTypography>
                  </MDBox>
                ))}
              </Grid>
            </>
          )}
        </Grid>
      </MDBox>
      <Footer />
    </DashboardLayout>
  );
}
