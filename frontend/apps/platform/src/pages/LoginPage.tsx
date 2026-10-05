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

const highlights = [
  { icon: "domain", title: "Company registry", text: "Create and manage every company from one console." },
  { icon: "lock", title: "Isolated workspaces", text: "Each company runs on its own process and database." },
  { icon: "sync", title: "Provisioning status", text: "Follow each workspace from request to active." },
];

// The page backdrop and brand panel keep their own fixed navy palette in both
// light and dark mode; only the form side follows the active theme.
const pageBackground = [
  "radial-gradient(60rem 40rem at 12% 8%, rgba(56, 189, 248, 0.22), transparent 60%)",
  "radial-gradient(50rem 36rem at 88% 92%, rgba(99, 102, 241, 0.26), transparent 60%)",
  "linear-gradient(135deg, #0B1220 0%, #111C33 55%, #0F172A 100%)",
].join(", ");

const gridOverlay = {
  content: '""',
  position: "absolute",
  inset: 0,
  backgroundImage:
    "linear-gradient(rgba(148, 163, 184, 0.09) 1px, transparent 1px), linear-gradient(90deg, rgba(148, 163, 184, 0.09) 1px, transparent 1px)",
  backgroundSize: "48px 48px",
  maskImage: "radial-gradient(ellipse at center, #000 30%, transparent 75%)",
  WebkitMaskImage: "radial-gradient(ellipse at center, #000 30%, transparent 75%)",
  pointerEvents: "none",
};

const currentYear = new Date().getFullYear();

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
      flexDirection="column"
      alignItems="center"
      justifyContent="center"
      px={2}
      py={4}
      sx={{
        position: "relative",
        overflow: "hidden",
        background: pageBackground,
        "&::before": gridOverlay,
      }}
    >
      <Card
        sx={{
          position: "relative",
          width: "100%",
          maxWidth: { xs: 520, md: 1120 },
          minHeight: { md: 680 },
          display: "grid",
          gridTemplateColumns: { xs: "1fr", md: "1fr 1fr" },
          overflow: "hidden",
          borderRadius: 4,
          boxShadow: "0 30px 80px -20px rgba(2, 6, 23, 0.7)",
        }}
      >
        <MDBox
          display={{ xs: "none", md: "flex" }}
          flexDirection="column"
          justifyContent="space-between"
          p={5}
          sx={{
            position: "relative",
            overflow: "hidden",
            color: "#fff",
            background: "linear-gradient(155deg, #0F172A 0%, #14213D 50%, #1E2A4A 100%)",
            "&::before, &::after": {
              content: '""',
              position: "absolute",
              borderRadius: 6,
              transform: "rotate(45deg)",
              background: "rgba(255, 255, 255, 0.05)",
              border: "1px solid rgba(255, 255, 255, 0.1)",
            },
            "&::before": { width: 300, height: 300, top: -130, right: -120 },
            "&::after": { width: 220, height: 220, bottom: -110, left: -90 },
          }}
        >
          <MDBox position="relative" zIndex={1}>
            <MDBox display="flex" alignItems="center" mb={5}>
              <MDBox
                display="flex"
                alignItems="center"
                justifyContent="center"
                mr={1.5}
                sx={{
                  flexShrink: 0,
                  width: 56,
                  height: 56,
                  borderRadius: 2,
                  color: "#fff",
                  background: "rgba(255, 255, 255, 0.18)",
                  border: "1px solid rgba(255, 255, 255, 0.28)",
                }}
              >
                <Icon sx={{ fontSize: "1.875rem !important" }}>storefront</Icon>
              </MDBox>
              <MDBox>
                <MDTypography variant="h3" fontWeight="bold" color="white" sx={{ lineHeight: 1.15 }}>
                  MP Seller Tools
                </MDTypography>
                <MDTypography
                  variant="button"
                  fontWeight="regular"
                  color="white"
                  display="block"
                  sx={{ mt: 0.25, opacity: 0.8 }}
                >
                  Market Place Tools
                </MDTypography>
              </MDBox>
            </MDBox>
            <MDTypography variant="h3" fontWeight="bold" color="white" sx={{ lineHeight: 1.2 }}>
              Platform Console
            </MDTypography>
            <MDTypography
              variant="body2"
              color="white"
              sx={{ mt: 1.5, maxWidth: 320, opacity: 0.85 }}
            >
              Manage companies, workspaces and provisioning from one place.
            </MDTypography>
          </MDBox>
          <MDBox position="relative" zIndex={1} mt={5}>
            {highlights.map(({ icon, title, text }) => (
              <MDBox key={title} display="flex" alignItems="flex-start" mt={2.5}>
                <MDBox
                  display="flex"
                  alignItems="center"
                  justifyContent="center"
                  mr={1.5}
                  sx={{
                    flexShrink: 0,
                    width: 36,
                    height: 36,
                    borderRadius: "50%",
                    color: "#fff",
                    background: "rgba(255, 255, 255, 0.16)",
                  }}
                >
                  <Icon fontSize="small">{icon}</Icon>
                </MDBox>
                <MDBox>
                  <MDTypography variant="button" fontWeight="medium" color="white" display="block">
                    {title}
                  </MDTypography>
                  <MDTypography
                    variant="caption"
                    color="white"
                    display="block"
                    sx={{ opacity: 0.8 }}
                  >
                    {text}
                  </MDTypography>
                </MDBox>
              </MDBox>
            ))}
          </MDBox>
        </MDBox>

        <MDBox
          display="flex"
          flexDirection="column"
          justifyContent="center"
          p={{ xs: 3, sm: 5 }}
          py={{ xs: 4, md: 7 }}
        >
          <MDBox display={{ xs: "flex", md: "none" }} alignItems="center" mb={3}>
            <MDBox
              display="flex"
              alignItems="center"
              justifyContent="center"
              mr={1.5}
              sx={{
                width: 40,
                height: 40,
                borderRadius: 2,
                color: "#fff",
                background: "linear-gradient(155deg, #0284C7 0%, #4338CA 100%)",
              }}
            >
              <Icon sx={{ fontSize: "1.5rem !important" }}>storefront</Icon>
            </MDBox>
            <MDBox>
              <MDTypography variant="h6" fontWeight="bold" sx={{ lineHeight: 1.2 }}>
                MP Seller Tools
              </MDTypography>
              <MDTypography variant="caption" color="text">
                Platform Console
              </MDTypography>
            </MDBox>
          </MDBox>

          <MDBox mb={4}>
            <MDTypography variant="h4" fontWeight="bold">
              Welcome back
            </MDTypography>
            <MDTypography variant="body2" color="text" sx={{ mt: 0.5 }}>
              Enter your administrator credentials to continue.
            </MDTypography>
          </MDBox>

          <MDBox component="form" role="form" onSubmit={handleSubmit}>
            {error && (
              <MDBox
                role="alert"
                display="flex"
                alignItems="center"
                mb={3}
                px={1.5}
                py={1.25}
                sx={{
                  borderRadius: 2,
                  color: "error.main",
                  background: "rgba(220, 38, 38, 0.1)",
                  border: "1px solid rgba(220, 38, 38, 0.35)",
                }}
              >
                <Icon fontSize="small" sx={{ mr: 1 }}>
                  error_outline
                </Icon>
                <MDTypography variant="button" color="error" fontWeight="regular">
                  {error}
                </MDTypography>
              </MDBox>
            )}
            <MDBox mb={2.5}>
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
                        sx={{ color: "text.main" }}
                      >
                        <Icon>{showPassword ? "visibility_off" : "visibility"}</Icon>
                      </IconButton>
                    </InputAdornment>
                  ),
                }}
              />
            </MDBox>
            <MDBox mt={4}>
              <MDButton
                type="submit"
                variant="gradient"
                color="info"
                size="large"
                fullWidth
                disabled={submitting}
              >
                Sign in
              </MDButton>
            </MDBox>
          </MDBox>

          <MDBox display="flex" alignItems="center" justifyContent="center" mt={4}>
            <Icon fontSize="small" sx={{ mr: 0.75, color: "text.main", opacity: 0.7 }}>
              verified_user
            </Icon>
            <MDTypography variant="caption" color="text">
              Restricted to authorized platform administrators.
            </MDTypography>
          </MDBox>
        </MDBox>
      </Card>

      <MDTypography
        variant="caption"
        sx={{ position: "relative", mt: 3, color: "rgba(226, 232, 240, 0.6)" }}
      >
        © {currentYear} MP Seller Tools
      </MDTypography>
    </MDBox>
  );
}
