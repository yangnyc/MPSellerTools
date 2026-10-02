import { useState } from "react";
import { useNavigate } from "react-router-dom";
import Card from "@mui/material/Card";
import IconButton from "@mui/material/IconButton";
import InputAdornment from "@mui/material/InputAdornment";
import Icon from "@mui/material/Icon";
import MDBox from "components/MDBox";
import MDTypography from "components/MDTypography";
import MDInput from "components/MDInput";
import MDButton from "components/MDButton";
import { useAuth } from "../auth/useAuth";

export default function LoginPage() {
  const { login } = useAuth();
  const navigate = useNavigate();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      await login(email, password);
      navigate("/dashboard", { replace: true });
    } catch {
      setError("Invalid email or password.");
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <MDBox
      minHeight="100vh"
      display="flex"
      alignItems="center"
      justifyContent="center"
      px={2}
      sx={{ backgroundColor: "grey.100" }}
    >
      <Card sx={{ width: "100%", maxWidth: 420, p: { xs: 3, sm: 4 } }}>
        <MDBox textAlign="center" mb={3}>
          <MDTypography variant="h4" fontWeight="medium">
            MPSellerTools
          </MDTypography>
          <MDTypography variant="body2" color="text">
            Platform Console
          </MDTypography>
        </MDBox>
        <MDBox component="form" role="form" onSubmit={handleSubmit}>
          {error && (
            <MDBox mb={2}>
              <MDTypography variant="caption" color="error">
                {error}
              </MDTypography>
            </MDBox>
          )}
          <MDBox mb={2}>
            <MDInput
              type="email"
              label="Email"
              fullWidth
              autoComplete="email"
              spellCheck={false}
              value={email}
              onChange={(event: React.ChangeEvent<HTMLInputElement>) =>
                setEmail(event.target.value)
              }
            />
          </MDBox>
          <MDBox mb={2}>
            <MDInput
              type={showPassword ? "text" : "password"}
              label="Password"
              fullWidth
              autoComplete="current-password"
              value={password}
              onChange={(event: React.ChangeEvent<HTMLInputElement>) =>
                setPassword(event.target.value)
              }
              InputProps={{
                endAdornment: (
                  <InputAdornment position="end">
                    <IconButton
                      aria-label={showPassword ? "Hide password" : "Show password"}
                      onClick={() => setShowPassword((show) => !show)}
                      edge="end"
                      size="small"
                    >
                      <Icon>{showPassword ? "visibility_off" : "visibility"}</Icon>
                    </IconButton>
                  </InputAdornment>
                ),
              }}
            />
          </MDBox>
          <MDBox mt={3}>
            <MDButton type="submit" variant="gradient" color="info" fullWidth disabled={submitting}>
              Sign in
            </MDButton>
          </MDBox>
        </MDBox>
      </Card>
    </MDBox>
  );
}
