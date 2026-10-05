import { useState } from "react";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import IconButton from "@mui/material/IconButton";
import InputAdornment from "@mui/material/InputAdornment";
import Tooltip from "@mui/material/Tooltip";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import {
  DetailList,
  InitialsAvatar,
  InlineAlert,
  PageHeader,
  Section,
  StatusPill,
  Surface,
  roleLabel,
  useKit,
} from "examples/Kit";
import PageShell from "../components/PageShell";
import { useAuth } from "../auth/useAuth";
import { useSnackbar } from "../components/useSnackbar";
import { apiFetch, ApiError } from "../lib/api";

const PASSWORD_RULES: { label: string; error: string; test: (password: string) => boolean }[] = [
  {
    label: "At least 12 characters",
    error: "New password must be at least 12 characters.",
    test: (password) => password.length >= 12,
  },
  {
    label: "An uppercase letter",
    error: "New password must include an uppercase letter.",
    test: (password) => /[A-Z]/.test(password),
  },
  {
    label: "A lowercase letter",
    error: "New password must include a lowercase letter.",
    test: (password) => /[a-z]/.test(password),
  },
  {
    label: "A number",
    error: "New password must include a number.",
    test: (password) => /[0-9]/.test(password),
  },
];

function validateNewPassword(password: string): string | null {
  return PASSWORD_RULES.find((rule) => !rule.test(password))?.error ?? null;
}

export default function ProfilePage() {
  const { user, logout, updateDisplayName } = useAuth();
  const { notify } = useSnackbar();
  const kit = useKit();
  const { c } = kit;

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
  const mismatch = Boolean(newPassword && confirmPassword && newPassword !== confirmPassword);
  const passwordError = mismatch
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
        sx={{ color: c.muted }}
      >
        <Icon fontSize="small">{showPasswords ? "visibility_off" : "visibility"}</Icon>
      </IconButton>
    </InputAdornment>
  );

  return (
    <PageShell>
      <PageHeader icon="person" title="Profile" subtitle="Your account details and password." />

      <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", lg: "2fr 3fr" }, alignItems: "start", gap: 3 }}>
        <Surface sx={{ p: 3 }}>
          <Box sx={{ display: "flex", alignItems: "center", gap: 2, mb: 3 }}>
            <InitialsAvatar name={user?.displayName || user?.email} size={64} />
            <Box sx={{ minWidth: 0, flex: 1 }}>
              {editingName ? (
                <Box sx={{ display: "flex", alignItems: "center", gap: 0.5 }}>
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
                  <IconButton size="small" color="info" aria-label="Save name" disabled={savingName} onClick={saveName}>
                    <Icon>check</Icon>
                  </IconButton>
                  <IconButton
                    size="small"
                    aria-label="Cancel"
                    disabled={savingName}
                    onClick={cancelEditingName}
                    sx={{ color: c.muted }}
                  >
                    <Icon>close</Icon>
                  </IconButton>
                </Box>
              ) : (
                <Box sx={{ display: "flex", alignItems: "center", gap: 0.5 }}>
                  <Box
                    component="h2"
                    sx={{
                      overflow: "hidden",
                      textOverflow: "ellipsis",
                      whiteSpace: "nowrap",
                      fontSize: "1.25rem",
                      fontWeight: 700,
                      color: c.text,
                    }}
                  >
                    {user?.displayName}
                  </Box>
                  <Tooltip title="Edit name">
                    <IconButton size="small" aria-label="Edit name" onClick={startEditingName} sx={{ color: c.muted }}>
                      <Icon fontSize="small">edit</Icon>
                    </IconButton>
                  </Tooltip>
                </Box>
              )}
              <Box sx={{ fontSize: "0.875rem", color: c.muted, overflowWrap: "anywhere" }}>{user?.email}</Box>
            </Box>
          </Box>
          <DetailList
            columns={1}
            items={[
              {
                label: "Role",
                value: user?.roles.length ? (
                  <Box sx={{ display: "flex", flexWrap: "wrap", gap: 0.75 }}>
                    {user.roles.map((role) => (
                      <StatusPill key={role} tone="primary" label={roleLabel(role)} />
                    ))}
                  </Box>
                ) : (
                  "—"
                ),
              },
            ]}
          />
        </Surface>

        <Section icon="lock" title="Change password" subtitle="Use a password you don't use anywhere else.">
          <Box
            component="form"
            noValidate
            onSubmit={(e: React.FormEvent) => {
              e.preventDefault();
              submit();
            }}
          >
            {error && <InlineAlert sx={{ mb: 2.5 }}>{error}</InlineAlert>}
            {/* Chrome's password manager pairs the nearest preceding text input with any
                password field on the page — without a dedicated username field right here,
                it can attach the saved password to an unrelated field instead. */}
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
            <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", md: "1fr 1fr" }, alignItems: "start", gap: 3 }}>
              <Box sx={{ display: "grid", gap: 2.5 }}>
                <MDInput
                  type={showPasswords ? "text" : "password"}
                  label="Current password"
                  autoComplete="current-password"
                  fullWidth
                  value={currentPassword}
                  onChange={(e: React.ChangeEvent<HTMLInputElement>) => setCurrentPassword(e.target.value)}
                  InputProps={{ endAdornment: passwordAdornment }}
                />
                <MDInput
                  type={showPasswords ? "text" : "password"}
                  label="New password"
                  autoComplete="new-password"
                  fullWidth
                  value={newPassword}
                  onChange={(e: React.ChangeEvent<HTMLInputElement>) => setNewPassword(e.target.value)}
                  InputProps={{ endAdornment: passwordAdornment }}
                />
                <MDInput
                  type={showPasswords ? "text" : "password"}
                  label="Confirm new password"
                  autoComplete="new-password"
                  fullWidth
                  error={mismatch}
                  value={confirmPassword}
                  onChange={(e: React.ChangeEvent<HTMLInputElement>) => setConfirmPassword(e.target.value)}
                  InputProps={{ endAdornment: passwordAdornment }}
                />
              </Box>
              <Box
                sx={{
                  p: 2,
                  borderRadius: "12px",
                  backgroundColor: c.surfaceAlt,
                  border: `1px solid ${c.border}`,
                }}
              >
                <Box sx={{ mb: 1, fontSize: "0.8125rem", fontWeight: 700, color: c.text }}>Your new password needs</Box>
                {[
                  ...PASSWORD_RULES.map((rule) => ({ label: rule.label, met: rule.test(newPassword) })),
                  { label: "To match the confirmation", met: Boolean(newPassword) && newPassword === confirmPassword },
                ].map((rule) => (
                  <Box
                    key={rule.label}
                    sx={{
                      display: "flex",
                      alignItems: "center",
                      gap: 1,
                      py: 0.375,
                      fontSize: "0.8125rem",
                      color: rule.met ? kit.tone("success").fg : c.muted,
                    }}
                  >
                    <Icon sx={{ fontSize: "1rem !important" }}>
                      {rule.met ? "check_circle" : "radio_button_unchecked"}
                    </Icon>
                    {rule.label}
                  </Box>
                ))}
              </Box>
            </Box>
            <Box sx={{ display: "flex", justifyContent: "flex-end", mt: 3 }}>
              <MDButton
                type="submit"
                variant="gradient"
                color="info"
                disabled={!passwordFieldsFilled || !!passwordError || submitting}
              >
                {submitting ? "Changing…" : "Change password"}
              </MDButton>
            </Box>
          </Box>
        </Section>
      </Box>
    </PageShell>
  );
}
