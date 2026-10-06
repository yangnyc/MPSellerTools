import { useEffect, useState } from "react";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import { PageHeader, Section, StateBlock, useKit } from "examples/Kit";
import PageShell from "../components/PageShell";
import { useAuth } from "../auth/useAuth";
import { useSnackbar } from "../components/useSnackbar";
import { ApiError } from "../lib/api";
import { SettingsApi, UsersApi } from "../api/resources";
import type { CompanySettings, UserSummary } from "../api/types";

const PLATFORM_MANAGED = [
  { icon: "link", title: "Workspace address", text: "The login URL your team uses." },
  { icon: "storage", title: "Database", text: "Your company's data lives in its own database." },
  { icon: "fingerprint", title: "Workspace identity", text: "The identifier that ties this workspace to your company." },
];

export default function SettingsPage() {
  const { logout } = useAuth();
  const { notify } = useSnackbar();
  const { c } = useKit();

  const [settings, setSettings] = useState<CompanySettings | null>(null);
  const [companyName, setCompanyName] = useState("");
  // Low-stock alerts: an empty level means they are off.
  const [threshold, setThreshold] = useState("");
  const [assigneeId, setAssigneeId] = useState("");
  const [users, setUsers] = useState<UserSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const show = (s: CompanySettings) => {
    setSettings(s);
    setCompanyName(s.companyName);
    setThreshold(s.lowStockThreshold === null ? "" : String(s.lowStockThreshold));
    setAssigneeId(s.lowStockAssigneeId ?? "");
  };

  useEffect(() => {
    Promise.all([SettingsApi.get(), UsersApi.list()])
      .then(([s, people]) => {
        show(s);
        setUsers(people.filter((u) => !u.isBlocked));
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
    const level = threshold.trim() === "" ? null : Number(threshold);
    if (level !== null && (!Number.isInteger(level) || level < 0)) {
      notify("The alert level is a whole number, 0 or more.", "error");
      return;
    }
    if (level !== null && !assigneeId) {
      notify("Choose who the restocking tasks go to.", "error");
      return;
    }
    setSaving(true);
    try {
      const updated = await SettingsApi.update({
        companyName,
        lowStockThreshold: level,
        lowStockAssigneeId: level === null ? null : assigneeId,
        rowVersion: settings.rowVersion,
      });
      show(updated);
      notify("Settings saved.", "success");
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        await logout();
        return;
      }
      notify(err instanceof ApiError ? err.message : "Save failed.", "error");
    } finally {
      setSaving(false);
    }
  };

  const savedThreshold = settings?.lowStockThreshold == null ? "" : String(settings.lowStockThreshold);
  const unchanged =
    settings?.companyName === companyName &&
    savedThreshold === threshold.trim() &&
    // With the alerts off the assignee is not saved, so changing it alone changes nothing.
    (threshold.trim() === "" || (settings?.lowStockAssigneeId ?? "") === assigneeId);

  return (
    <PageShell>
      <PageHeader icon="settings" title="Settings" subtitle="How your company appears across this workspace." />

      <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", lg: "3fr 2fr" }, alignItems: "start", gap: 3 }}>
        <Section icon="business" title="Company settings" subtitle="Visible to everyone in your company.">
          {loading && <StateBlock kind="loading" title="Loading settings" />}
          {error && <StateBlock kind="error" title="Settings could not be loaded" message={error} />}
          {!loading && !error && (
            <Box
              component="form"
              noValidate
              onSubmit={(e: React.FormEvent) => {
                e.preventDefault();
                save();
              }}
            >
              <MDInput
                label="Company name"
                fullWidth
                value={companyName}
                onChange={(e: React.ChangeEvent<HTMLInputElement>) => setCompanyName(e.target.value)}
              />
              <Box sx={{ mt: 3, pt: 2.5, borderTop: `1px solid ${c.border}` }}>
                <Box sx={{ fontSize: "0.875rem", fontWeight: 700, color: c.text }}>Low-stock alerts</Box>
                <Box sx={{ mb: 2.5, fontSize: "0.8125rem", lineHeight: 1.5, color: c.muted }}>
                  A product with this many units or fewer left to sell gets a restocking task, once, until that task is done. Leave the level
                  empty to switch the alerts off.
                </Box>
                <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", sm: "1fr 2fr" }, gap: 2.5 }}>
                  <MDInput
                    label="Alert level"
                    type="number"
                    fullWidth
                    inputProps={{ min: 0, step: 1 }}
                    value={threshold}
                    onChange={(e: React.ChangeEvent<HTMLInputElement>) => setThreshold(e.target.value)}
                  />
                  <MDInput
                    select
                    label="Assign restocking tasks to"
                    fullWidth
                    SelectProps={{ native: true }}
                    InputLabelProps={{ shrink: true }}
                    disabled={threshold.trim() === ""}
                    value={assigneeId}
                    onChange={(e: React.ChangeEvent<HTMLInputElement>) => setAssigneeId(e.target.value)}
                  >
                    <option value="">Choose a person…</option>
                    {users.map((u) => (
                      <option key={u.id} value={u.id}>
                        {u.displayName || u.email}
                      </option>
                    ))}
                  </MDInput>
                </Box>
              </Box>
              <Box sx={{ display: "flex", justifyContent: "flex-end", gap: 1, mt: 3 }}>
                <MDButton
                  variant="text"
                  color="secondary"
                  disabled={unchanged || saving}
                  onClick={() => settings && show(settings)}
                >
                  Reset
                </MDButton>
                <MDButton type="submit" variant="gradient" color="info" disabled={unchanged || saving}>
                  {saving ? "Saving…" : "Save"}
                </MDButton>
              </Box>
            </Box>
          )}
        </Section>

        <Section
          icon="lock"
          tone="neutral"
          title="Managed by the platform"
          subtitle="Fixed when the workspace was created. They cannot be changed here."
        >
          <Box sx={{ display: "grid", gap: 2 }}>
            {PLATFORM_MANAGED.map((item) => (
              <Box key={item.title} sx={{ display: "flex", alignItems: "flex-start", gap: 1.5 }}>
                <Icon sx={{ mt: "2px", fontSize: "1.25rem !important", color: c.subtle }}>{item.icon}</Icon>
                <Box sx={{ lineHeight: 1.45 }}>
                  <Box sx={{ fontSize: "0.875rem", fontWeight: 500, color: c.text }}>{item.title}</Box>
                  <Box sx={{ fontSize: "0.8125rem", color: c.muted }}>{item.text}</Box>
                </Box>
              </Box>
            ))}
          </Box>
        </Section>
      </Box>
    </PageShell>
  );
}
