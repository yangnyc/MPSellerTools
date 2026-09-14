import { useState } from "react";
import Card from "@mui/material/Card";
import MDBox from "components/MDBox";
import MDTypography from "components/MDTypography";
import MDInput from "components/MDInput";
import MDButton from "components/MDButton";

export default function LoginPage() {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");

  const handleSubmit = (event: React.FormEvent) => {
    event.preventDefault();
    // Wired to POST /api/auth/login with cookie session + antiforgery in Increment 2.
    // No role or tenant selector here (brief §8) — the tenant is fixed by which
    // TenantHost instance/port served this page.
  };

  return (
    <MDBox
      minHeight="100vh"
      display="flex"
      alignItems="center"
      justifyContent="center"
      sx={{ backgroundColor: "grey.100" }}
    >
      <Card sx={{ width: "100%", maxWidth: 420, p: 4 }}>
        <MDBox textAlign="center" mb={3}>
          <MDTypography variant="h4" fontWeight="medium">
            MPSellerTools
          </MDTypography>
          <MDTypography variant="body2" color="text">
            Sign in to your company workspace
          </MDTypography>
        </MDBox>
        <MDBox component="form" role="form" onSubmit={handleSubmit}>
          <MDBox mb={2}>
            <MDInput
              type="email"
              label="Email"
              fullWidth
              value={email}
              onChange={(event: React.ChangeEvent<HTMLInputElement>) =>
                setEmail(event.target.value)
              }
            />
          </MDBox>
          <MDBox mb={2}>
            <MDInput
              type="password"
              label="Password"
              fullWidth
              value={password}
              onChange={(event: React.ChangeEvent<HTMLInputElement>) =>
                setPassword(event.target.value)
              }
            />
          </MDBox>
          <MDBox mt={3}>
            <MDButton type="submit" variant="gradient" color="info" fullWidth>
              Sign in
            </MDButton>
          </MDBox>
        </MDBox>
      </Card>
    </MDBox>
  );
}
