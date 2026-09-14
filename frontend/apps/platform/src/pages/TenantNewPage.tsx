import { useState } from "react";
import { useNavigate } from "react-router-dom";
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
import { ApiError } from "../lib/api";
import { TenantsApi } from "../api/resources";

export default function TenantNewPage() {
  const { logout } = useAuth();
  const { notify } = useSnackbar();
  const navigate = useNavigate();

  const [name, setName] = useState("");
  const [slug, setSlug] = useState("");
  const [email, setEmail] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const submit = async () => {
    setError(null);
    if (!name.trim() || !slug.trim() || !email.trim()) {
      setError("All fields are required.");
      return;
    }
    setSubmitting(true);
    try {
      const result = await TenantsApi.create({ name, slug, initialAdminEmail: email });
      notify("Company creation started. Provisioning is in progress.", "success");
      navigate(`/tenants/${result.tenantId}`);
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        await logout();
        return;
      }
      setError(err instanceof ApiError ? err.message : "Failed to create company.");
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <DashboardLayout>
      <DashboardNavbar onLogout={logout} />
      <MDBox py={3}>
        <Card sx={{ maxWidth: 480 }}>
          <MDBox p={3}>
            <MDTypography variant="h5" mb={2}>
              Create company
            </MDTypography>
            <MDTypography variant="body2" color="text" mb={3}>
              The system assigns the port and local login URL automatically — no server details needed.
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
                label="Company name"
                fullWidth
                value={name}
                onChange={(e: React.ChangeEvent<HTMLInputElement>) => setName(e.target.value)}
              />
            </MDBox>
            <MDBox mb={2}>
              <MDInput
                label="Slug"
                fullWidth
                helperText="Lowercase letters, digits, hyphens only"
                value={slug}
                onChange={(e: React.ChangeEvent<HTMLInputElement>) => setSlug(e.target.value)}
              />
            </MDBox>
            <MDBox mb={3}>
              <MDInput
                label="Initial administrator email"
                type="email"
                fullWidth
                value={email}
                onChange={(e: React.ChangeEvent<HTMLInputElement>) => setEmail(e.target.value)}
              />
            </MDBox>
            <MDButton variant="gradient" color="info" onClick={submit} disabled={submitting}>
              Create company
            </MDButton>
          </MDBox>
        </Card>
      </MDBox>
      <Footer />
    </DashboardLayout>
  );
}
