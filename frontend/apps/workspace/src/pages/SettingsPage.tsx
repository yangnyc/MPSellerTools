import { useEffect, useState } from "react";
import Card from "@mui/material/Card";
import MDBox from "components/MDBox";
import MDTypography from "components/MDTypography";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import DashboardLayout from "examples/LayoutContainers/DashboardLayout";
import WorkspaceNavbar from "../components/WorkspaceNavbar";
import Footer from "examples/Footer";
import { useAuth } from "../auth/useAuth";
import { useSnackbar } from "../components/useSnackbar";
import { ApiError } from "../lib/api";
import { SettingsApi } from "../api/resources";
import type { CompanySettings } from "../api/types";

export default function SettingsPage() {
  const { logout } = useAuth();
  const { notify } = useSnackbar();

  const [settings, setSettings] = useState<CompanySettings | null>(null);
  const [companyName, setCompanyName] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    SettingsApi.get()
      .then((s) => {
        setSettings(s);
        setCompanyName(s.companyName);
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load settings."))
      .finally(() => setLoading(false));
  }, []);

  const save = async () => {
    if (!settings) return;
    if (!companyName.trim()) {
      notify("Company name is required.", "error");
      return;
    }
    try {
      const updated = await SettingsApi.update({ companyName, rowVersion: settings.rowVersion });
      setSettings(updated);
      notify("Settings saved.", "success");
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        await logout();
        return;
      }
      notify(err instanceof ApiError ? err.message : "Save failed.", "error");
    }
  };

  return (
    <DashboardLayout>
      <WorkspaceNavbar onLogout={logout} />
      <MDBox py={3}>
        <Card sx={{ maxWidth: 480 }}>
          <MDBox p={3}>
            <MDTypography variant="h5" mb={2}>
              Company settings
            </MDTypography>
            {loading && <MDTypography variant="body2">Loading…</MDTypography>}
            {error && (
              <MDTypography variant="body2" color="error">
                {error}
              </MDTypography>
            )}
            {!loading && !error && (
              <>
                <MDBox mb={3}>
                  <MDInput
                    label="Company name"
                    fullWidth
                    value={companyName}
                    onChange={(e: React.ChangeEvent<HTMLInputElement>) => setCompanyName(e.target.value)}
                  />
                </MDBox>
                <MDButton variant="gradient" color="info" onClick={save}>
                  Save
                </MDButton>
              </>
            )}
          </MDBox>
        </Card>
      </MDBox>
      <Footer />
    </DashboardLayout>
  );
}
