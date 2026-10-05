import { useCallback, useEffect, useState } from "react";
import { useParams } from "react-router-dom";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import IconButton from "@mui/material/IconButton";
import LinearProgress from "@mui/material/LinearProgress";
import Link from "@mui/material/Link";
import Tooltip from "@mui/material/Tooltip";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import {
  DetailList,
  InitialsAvatar,
  InlineAlert,
  PageHeader,
  Section,
  StateBlock,
  StatusPill,
  Surface,
  formatDateTime,
  timeAgo,
  useKit,
} from "examples/Kit";
import PageShell from "../components/PageShell";
import { useAuth } from "../auth/useAuth";
import { useSnackbar } from "../components/useSnackbar";
import ConfirmDialog from "../components/ConfirmDialog";
import { ApiError } from "../lib/api";
import { TenantsApi } from "../api/resources";
import { TENANT_STATUS_LABELS, type TenantDetail, type TenantStatus } from "../api/types";
import { TENANT_STATUS_TONE } from "../lib/status";

type Action = "suspend" | "resume" | "retry";

const ACTIONS: Record<Action, { title: string; label: string; icon: string; color: "warning" | "success" | "info" }> = {
  suspend: { title: "Suspend company", label: "Suspend", icon: "pause_circle", color: "warning" },
  resume: { title: "Resume company", label: "Resume", icon: "play_circle", color: "success" },
  retry: { title: "Retry provisioning", label: "Retry provisioning", icon: "refresh", color: "info" },
};

// The one lifecycle action the server accepts in each state (none while provisioning).
const ACTION_FOR_STATUS: Record<TenantStatus, Action | null> = { 0: null, 1: "suspend", 2: "resume", 3: "retry" };

const STATE_NOTES: Record<TenantStatus, string> = {
  0: "The provisioning worker is creating this company's database and starting its workspace.",
  1: "The workspace is running and its team can sign in. Suspending stops the instance until you resume it.",
  2: "The workspace is stopped and nobody can sign in. Resuming starts it again and checks that it is ready.",
  3: "Provisioning did not finish. Retrying picks up where it stopped without creating anything twice.",
};

export default function TenantDetailPage() {
  const { id } = useParams<{ id: string }>();
  const { logout } = useAuth();
  const { notify } = useSnackbar();
  const { c } = useKit();

  const [tenant, setTenant] = useState<TenantDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [confirmAction, setConfirmAction] = useState<Action | null>(null);

  const [editingName, setEditingName] = useState(false);
  const [nameDraft, setNameDraft] = useState("");
  const [savingName, setSavingName] = useState(false);

  const load = useCallback(() => {
    if (!id) return;
    TenantsApi.get(id)
      .then((value) => { setTenant(value); setError(null); })
      .catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load company."));
  }, [id]);

  useEffect(load, [load]);
  // Poll while provisioning so the page updates itself once the worker finishes.
  useEffect(() => {
    if (tenant?.status !== 0) return;
    const timer = setInterval(load, 3000);
    return () => clearInterval(timer);
  }, [tenant?.status, load]);

  const startEditingName = () => {
    setNameDraft(tenant?.name ?? "");
    setEditingName(true);
  };

  const cancelEditingName = () => {
    setEditingName(false);
    setNameDraft(tenant?.name ?? "");
  };

  const saveName = async () => {
    if (!id) return;
    const trimmed = nameDraft.trim();
    if (!trimmed) {
      notify("Name can't be empty.", "error");
      return;
    }
    setSavingName(true);
    try {
      const updated = await TenantsApi.update(id, { name: trimmed });
      setTenant(updated);
      notify("Company renamed.", "success");
      setEditingName(false);
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        await logout();
        return;
      }
      notify(err instanceof ApiError ? err.message : "Failed to rename company.", "error");
    } finally {
      setSavingName(false);
    }
  };

  const runAction = async () => {
    if (!id || !confirmAction) return;
    try {
      if (confirmAction === "suspend") await TenantsApi.suspend(id);
      if (confirmAction === "resume") await TenantsApi.resume(id);
      if (confirmAction === "retry") await TenantsApi.retryProvisioning(id);
      notify("Action started.", "success");
      setConfirmAction(null);
      load();
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        await logout();
        return;
      }
      notify(err instanceof ApiError ? err.message : "Action failed.", "error");
      setConfirmAction(null);
    }
  };

  const copyUrl = async () => {
    if (!tenant?.url) return;
    try {
      await navigator.clipboard.writeText(tenant.url);
      notify("Login URL copied.", "success");
    } catch {
      notify("Could not copy the URL. Select it and copy manually.", "error");
    }
  };

  if (error || !tenant) {
    return (
      <PageShell>
        <PageHeader icon="apartment" title="Company" backTo="/tenants" backLabel="All tenants" />
        <Surface>
          {error ? (
            <StateBlock
              kind="error"
              title="This company could not be loaded"
              message={error}
              action={
                <MDButton variant="outlined" color="info" size="small" onClick={load}>
                  Try again
                </MDButton>
              }
            />
          ) : (
            <StateBlock kind="loading" title="Loading company" />
          )}
        </Surface>
      </PageShell>
    );
  }

  const action = ACTION_FOR_STATUS[tenant.status];
  const actionButton = action && (
    <MDButton
      variant="gradient"
      color={ACTIONS[action].color}
      startIcon={<Icon>{ACTIONS[action].icon}</Icon>}
      onClick={() => setConfirmAction(action)}
    >
      {ACTIONS[action].label}
    </MDButton>
  );

  return (
    <PageShell>
      <PageHeader
        backTo="/tenants"
        backLabel="All tenants"
        title={
          editingName ? (
            <Box sx={{ display: "flex", alignItems: "center", gap: 0.5 }}>
              <MDInput
                size="small"
                label="Company name"
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
            <Box sx={{ display: "flex", alignItems: "center", gap: 1.5 }}>
              <InitialsAvatar name={tenant.name} size={48} square />
              <span>{tenant.name}</span>
              <Tooltip title="Rename">
                <IconButton size="small" aria-label="Edit name" onClick={startEditingName} sx={{ color: c.muted }}>
                  <Icon fontSize="small">edit</Icon>
                </IconButton>
              </Tooltip>
            </Box>
          )
        }
        actions={
          <StatusPill
            tone={TENANT_STATUS_TONE[tenant.status]}
            label={TENANT_STATUS_LABELS[tenant.status]}
            pulse={tenant.status === 0}
          />
        }
      />

      {tenant.status === 0 && (
        <Surface sx={{ mb: 3, p: 2.5 }}>
          <Box sx={{ mb: 1.5, fontSize: "0.875rem", fontWeight: 500, color: c.text }}>
            Provisioning in progress — this page updates automatically.
          </Box>
          <LinearProgress color="info" sx={{ borderRadius: 3 }} />
        </Surface>
      )}
      {tenant.failureReason && (
        <InlineAlert title="Provisioning failed" sx={{ mb: 3 }}>
          {tenant.failureReason}
        </InlineAlert>
      )}

      <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", lg: "3fr 2fr" }, alignItems: "start", gap: 3 }}>
        <Section icon="dns" title="Workspace" subtitle="How this company's team reaches its instance.">
          <DetailList
            items={[
              {
                label: "Login URL",
                value: tenant.url ? (
                  <Box sx={{ display: "flex", alignItems: "center", gap: 0.5 }}>
                    <Link href={tenant.url} target="_blank" rel="noreferrer" sx={{ color: c.accent }}>
                      {tenant.url}
                    </Link>
                    <Tooltip title="Copy">
                      <IconButton size="small" aria-label="Copy login URL" onClick={copyUrl} sx={{ color: c.muted }}>
                        <Icon sx={{ fontSize: "1rem !important" }}>content_copy</Icon>
                      </IconButton>
                    </Tooltip>
                  </Box>
                ) : (
                  <Box component="span" sx={{ color: c.muted }}>
                    Not yet assigned
                  </Box>
                ),
              },
              {
                label: "Slug",
                value: (
                  <Box component="span" sx={{ fontFamily: "monospace" }}>
                    {tenant.slug}
                  </Box>
                ),
              },
              { label: "Created", value: formatDateTime(tenant.createdAtUtc) },
              {
                label: "Last updated",
                value: <span title={formatDateTime(tenant.updatedAtUtc)}>{timeAgo(tenant.updatedAtUtc)}</span>,
              },
            ]}
          />
        </Section>

        <Section icon="tune" tone={TENANT_STATUS_TONE[tenant.status]} title="Lifecycle" subtitle={TENANT_STATUS_LABELS[tenant.status]}>
          <Box sx={{ fontSize: "0.875rem", lineHeight: 1.6, color: c.muted }}>{STATE_NOTES[tenant.status]}</Box>
          {actionButton && <Box sx={{ mt: 2.5 }}>{actionButton}</Box>}
        </Section>
      </Box>

      <ConfirmDialog
        open={!!confirmAction}
        title={confirmAction ? ACTIONS[confirmAction].title : ""}
        message={`Are you sure you want to ${confirmAction} "${tenant.name}"?`}
        confirmLabel={confirmAction === "retry" ? "Retry" : confirmAction ? ACTIONS[confirmAction].label : ""}
        confirmColor={confirmAction === "suspend" ? "warning" : "info"}
        onConfirm={runAction}
        onCancel={() => setConfirmAction(null)}
      />
    </PageShell>
  );
}
