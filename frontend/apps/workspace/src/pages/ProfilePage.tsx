import { useState } from "react";
import Card from "@mui/material/Card";
import Icon from "@mui/material/Icon";
import IconButton from "@mui/material/IconButton";
import InputAdornment from "@mui/material/InputAdornment";
import MDBox from "components/MDBox";
import MDTypography from "components/MDTypography";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import DashboardLayout from "examples/LayoutContainers/DashboardLayout";
import WorkspaceNavbar from "../components/WorkspaceNavbar";
import Footer from "examples/Footer";
import { useAuth } from "../auth/useAuth";
import { useSnackbar } from "../components/useSnackbar";
import { apiFetch, ApiError } from "../lib/api";

const PASSWORD_RULES = "At least 12 characters, with an uppercase letter, a lowercase letter, and a number.";

function validateNewPassword(password: string): string | null {
  if (password.length < 12) return "New password must be at least 12 characters.";
  if (!/[A-Z]/.test(password)) return "New password must include an uppercase letter.";
  if (!/[a-z]/.test(password)) return "New password must include a lowercase letter.";
  if (!/[0-9]/.test(password)) return "New password must include a number.";
  return null;
}

export default function ProfilePage() {
  const { user, logout, updateDisplayName } = useAuth();
  const { notify } = useSnackbar();

  const [editingName, setEditingName] = useState(false);
  const [nameDraft, setNameDraft] = useState(user?.displayName ?? "");
  const [savingName, setSavingName] = useState(false);

  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [showPasswords, setShowPasswords] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const startEditingName = () => {
    setNameDraft(user?.displayName ?? "");
    setEditingName(true);
  };

  const cancelEditingName = () => {
    setEditingName(false);
    setNameDraft(user?.displayName ?? "");
  };

  const saveName = async () => {
    const trimmed = nameDraft.trim();
    if (!trimmed) {
      notify("Name can't be empty.", "error");
      return;
    }
    setSavingName(true);
    try {
      await updateDisplayName(trimmed);
      notify("Name updated.", "success");
      setEditingName(false);
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        await logout();
        return;
      }
      notify(err instanceof ApiError ? err.message : "Failed to update name.", "error");
    } finally {
      setSavingName(false);
    }
  };

  const passwordFieldsFilled = currentPassword && newPassword && confirmPassword;
  const passwordError =
    newPassword && confirmPassword && newPassword !== confirmPassword
      ? "New password and confirmation do not match."
      : newPassword
        ? validateNewPassword(newPassword)
        : null;

  const submit = async () => {
    setError(null);

    if (newPassword !== confirmPassword) {
      setError("New password and confirmation do not match.");
      return;
    }
    const validationError = validateNewPassword(newPassword);
    if (validationError) {
      setError(validationError);
      return;
    }

    setSubmitting(true);
    try {
      await apiFetch<void>("/api/auth/change-password", {
        method: "POST",
        body: JSON.stringify({ currentPassword, newPassword }),
      });
      notify("Password changed.", "success");
      setCurrentPassword("");
      setNewPassword("");
      setConfirmPassword("");
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        await logout();
        return;
      }
      setError(err instanceof ApiError ? err.message : "Password change failed.");
    } finally {
      setSubmitting(false);
    }
  };

  const passwordAdornment = (
    <InputAdornment position="end">
      <IconButton
        size="small"
        disableRipple
        aria-label={showPasswords ? "Hide passwords" : "Show passwords"}
        onClick={() => setShowPasswords((v) => !v)}
      >
        <Icon fontSize="small">{showPasswords ? "visibility_off" : "visibility"}</Icon>
      </IconButton>
    </InputAdornment>
  );

  return (
    <DashboardLayout>
      <WorkspaceNavbar onLogout={logout} />
      <MDBox py={3}>
        <Card sx={{ maxWidth: 480, mb: 3 }}>
          <MDBox p={3}>
            <MDTypography variant="h5" mb={2}>
              Profile
            </MDTypography>

            {editingName ? (
              <MDBox display="flex" alignItems="center" gap={1} mb={1}>
                <MDInput
                  size="small"
                  label="Name"
                  fullWidth
                  autoFocus
                  value={nameDraft}
                  onChange={(e: React.ChangeEvent<HTMLInputElement>) => setNameDraft(e.target.value)}
                  onKeyDown={(e: React.KeyboardEvent) => {
                    if (e.key === "Enter") saveName();
                    if (e.key === "Escape") cancelEditingName();
                  }}
                />
                <IconButton
                  size="small"
                  color="info"
                  aria-label="Save name"
                  disabled={savingName}
                  onClick={saveName}
                >
                  <Icon>check</Icon>
                </IconButton>
                <IconButton size="small" aria-label="Cancel" disabled={savingName} onClick={cancelEditingName}>
                  <Icon>close</Icon>
                </IconButton>
              </MDBox>
            ) : (
              <MDBox display="flex" alignItems="center" gap={1}>
                <MDTypography variant="body2">Name: {user?.displayName}</MDTypography>
                <IconButton size="small" aria-label="Edit name" onClick={startEditingName}>
                  <Icon fontSize="small">edit</Icon>
                </IconButton>
              </MDBox>
            )}

            <MDTypography variant="body2">Email: {user?.email}</MDTypography>
            <MDTypography variant="body2">Role: {user?.roles.join(", ") || "—"}</MDTypography>
          </MDBox>
        </Card>
        <Card sx={{ maxWidth: 480 }}>
          <MDBox
            component="form"
            p={3}
            onSubmit={(e: React.FormEvent) => {
              e.preventDefault();
              submit();
            }}
          >
            <MDTypography variant="h5" mb={2}>
              Change password
            </MDTypography>
            <MDTypography variant="caption" color="text" mb={2} display="block">
              {PASSWORD_RULES}
            </MDTypography>
            {error && (
              <MDBox mb={2}>
                <MDTypography variant="caption" color="error">
                  {error}
                </MDTypography>
              </MDBox>
            )}
            {/* Chrome's password manager pairs the nearest preceding text input with any
                password field on the page — without a dedicated username field right here,
                it reaches past this form for the navbar search box instead. */}
            <input
              type="text"
              name="username"
              autoComplete="username"
              value={user?.email ?? ""}
              readOnly
              aria-hidden="true"
              tabIndex={-1}
              style={{ position: "absolute", width: 1, height: 1, padding: 0, margin: -1, overflow: "hidden", clip: "rect(0,0,0,0)", whiteSpace: "nowrap", border: 0 }}
            />
            <MDBox mb={2}>
              <MDInput
                type={showPasswords ? "text" : "password"}
                label="Current password"
                autoComplete="current-password"
                fullWidth
                value={currentPassword}
                onChange={(e: React.ChangeEvent<HTMLInputElement>) => setCurrentPassword(e.target.value)}
                InputProps={{ endAdornment: passwordAdornment }}
              />
            </MDBox>
            <MDBox mb={2}>
              <MDInput
                type={showPasswords ? "text" : "password"}
                label="New password"
                autoComplete="new-password"
                fullWidth
                value={newPassword}
                onChange={(e: React.ChangeEvent<HTMLInputElement>) => setNewPassword(e.target.value)}
                InputProps={{ endAdornment: passwordAdornment }}
              />
            </MDBox>
            <MDBox mb={1}>
              <MDInput
                type={showPasswords ? "text" : "password"}
                label="Confirm new password"
                autoComplete="new-password"
                fullWidth
                value={confirmPassword}
                onChange={(e: React.ChangeEvent<HTMLInputElement>) => setConfirmPassword(e.target.value)}
                InputProps={{ endAdornment: passwordAdornment }}
              />
            </MDBox>
            {passwordError && (
              <MDBox mb={2}>
                <MDTypography variant="caption" color="error">
                  {passwordError}
                </MDTypography>
              </MDBox>
            )}
            <MDBox mt={2}>
              <MDButton
                type="submit"
                variant="gradient"
                color="info"
                disabled={!passwordFieldsFilled || !!passwordError || submitting}
              >
                {submitting ? "Changing…" : "Change password"}
              </MDButton>
            </MDBox>
          </MDBox>
        </Card>
      </MDBox>
      <Footer />
    </DashboardLayout>
  );
}
