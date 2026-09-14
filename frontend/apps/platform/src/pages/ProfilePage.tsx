import { useState } from "react";
import Card from "@mui/material/Card";
import MDBox from "components/MDBox";
import MDTypography from "components/MDTypography";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import DashboardLayout from "examples/LayoutContainers/DashboardLayout";
import DashboardNavbar from "examples/Navbars/DashboardNavbar";
import Footer from "examples/Footer";
import { useAuth } from "../auth/useAuth";
import { useSnackbar } from "../components/useSnackbar";
import { apiFetch, ApiError } from "../lib/api";

export default function ProfilePage() {
  const { user, logout } = useAuth();
  const { notify } = useSnackbar();
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [error, setError] = useState<string | null>(null);

  const submit = async () => {
    setError(null);
    try {
      await apiFetch<void>("/api/auth/change-password", {
        method: "POST",
        body: JSON.stringify({ currentPassword, newPassword }),
      });
      notify("Password changed.", "success");
      setCurrentPassword("");
      setNewPassword("");
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        await logout();
        return;
      }
      setError(err instanceof ApiError ? err.message : "Password change failed.");
    }
  };

  return (
    <DashboardLayout>
      <DashboardNavbar onLogout={logout} />
      <MDBox py={3}>
        <Card sx={{ maxWidth: 480, mb: 3 }}>
          <MDBox p={3}>
            <MDTypography variant="h5" mb={2}>
              Profile
            </MDTypography>
            <MDTypography variant="body2">Name: {user?.displayName}</MDTypography>
            <MDTypography variant="body2">Email: {user?.email}</MDTypography>
          </MDBox>
        </Card>
        <Card sx={{ maxWidth: 480 }}>
          <MDBox p={3}>
            <MDTypography variant="h5" mb={2}>
              Change password
            </MDTypography>
            {error && (
              <MDBox mb={2}>
                <MDTypography variant="caption" color="error">
                  {error}
                </MDTypography>
              </MDBox>
            )}
            <MDBox mb={2}>
              <MDInput
                type="password"
                label="Current password"
                fullWidth
                value={currentPassword}
                onChange={(e: React.ChangeEvent<HTMLInputElement>) => setCurrentPassword(e.target.value)}
              />
            </MDBox>
            <MDBox mb={3}>
              <MDInput
                type="password"
                label="New password"
                fullWidth
                value={newPassword}
                onChange={(e: React.ChangeEvent<HTMLInputElement>) => setNewPassword(e.target.value)}
              />
            </MDBox>
            <MDButton variant="gradient" color="info" onClick={submit}>
              Change password
            </MDButton>
          </MDBox>
        </Card>
      </MDBox>
      <Footer />
    </DashboardLayout>
  );
}
