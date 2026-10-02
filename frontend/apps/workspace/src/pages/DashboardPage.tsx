import { useEffect, useState } from "react";
import Grid from "@mui/material/Grid";
import MDBox from "components/MDBox";
import MDTypography from "components/MDTypography";
import ComplexStatisticsCard from "examples/Cards/StatisticsCards/ComplexStatisticsCard";
import DashboardLayout from "examples/LayoutContainers/DashboardLayout";
import WorkspaceNavbar from "../components/WorkspaceNavbar";
import Footer from "examples/Footer";
import { useAuth } from "../auth/useAuth";
import { DashboardApi } from "../api/resources";
import type { TenantDashboard } from "../api/types";

export default function DashboardPage() {
  const { user, logout } = useAuth();
  const isTenantAdmin = user?.roles.includes("TenantAdmin") ?? false;
  const [data, setData] = useState<TenantDashboard | null>(null);

  useEffect(() => {
    DashboardApi.get()
      .then(setData)
      .catch(() => setData(null));
  }, []);

  return (
    <DashboardLayout>
      <WorkspaceNavbar onLogout={logout} />
      <MDBox py={3}>
        <Grid container spacing={3}>
          <Grid item xs={12}>
            <MDTypography variant="h4" fontWeight="medium">
              {isTenantAdmin ? "Company Dashboard" : "My Dashboard"}
            </MDTypography>
            <MDTypography variant="body2" color="text" mb={2}>
              {isTenantAdmin
                ? "Metrics calculated from your company's products, orders, and tasks."
                : "Your assigned orders and tasks."}
            </MDTypography>
          </Grid>
          {data && (
            <>
              {isTenantAdmin && (
                <Grid item xs={12} md={6} lg={3}>
                  <ComplexStatisticsCard
                    color="info"
                    icon="inventory_2"
                    title="Catalog"
                    count={data.productCount}
                    percentage={{ color: "info", amount: "", label: "active products" }}
                  />
                </Grid>
              )}
              <Grid item xs={12} md={6} lg={3}>
                <ComplexStatisticsCard
                  color="success"
                  icon="receipt_long"
                  title="Open orders"
                  count={data.openOrderCount}
                  percentage={{ color: "success", amount: "", label: `of ${data.totalOrderCount} total` }}
                />
              </Grid>
              <Grid item xs={12} md={6} lg={3}>
                <ComplexStatisticsCard
                  color="warning"
                  icon="checklist"
                  title="Open tasks"
                  count={data.openTaskCount}
                  percentage={{ color: "warning", amount: "", label: `of ${data.totalTaskCount} total` }}
                />
              </Grid>
            </>
          )}
        </Grid>
      </MDBox>
      <Footer />
    </DashboardLayout>
  );
}
