import { useCallback, useEffect, useState } from "react";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import MDButton from "components/MDButton";
import { Identity, PageHeader, Section, SimpleTable, StateBlock, StatusPill, formatDateTime, timeAgo, useKit } from "examples/Kit";
import PageShell from "../../components/PageShell";
import ConfirmDialog from "../../components/ConfirmDialog";
import { useSnackbar } from "../../components/useSnackbar";
import { ApiError } from "../../lib/api";
import { TENANT_STATUS_TONE } from "../../lib/status";
import { TenantsApi } from "../../api/resources";
import { TENANT_STATUS_LABELS, type TenantRuntime } from "../../api/types";

export default function TenantControlsPage() {
  const { c } = useKit();
  const { notify } = useSnackbar();
  const [tenants, setTenants] = useState<TenantRuntime[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [restartTarget, setRestartTarget] = useState<TenantRuntime | null>(null);

  const fetchData = useCallback(() => {
    TenantsApi.runtime()
      .then(setTenants)
      .catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load companies."))
      .finally(() => setLoading(false));
  }, []);

  useEffect(fetchData, [fetchData]);

  const confirmRestart = async () => {
    if (!restartTarget) return;
    const target = restartTarget;
    setRestartTarget(null);
    try {
      await TenantsApi.restart(target.id);
      notify(`${target.name} queued to restart.`, "success");
      fetchData();
    } catch (err) {
      notify(err instanceof ApiError ? err.message : "Restart failed.", "error");
    }
  };

  const none = (
    <Box component="span" sx={{ color: c.subtle }}>
      —
    </Box>
  );
  const mono = { fontFamily: "monospace", fontSize: "0.8125rem" };

  return (
    <PageShell>
      <PageHeader
        icon="settings_power"
        title="Tenant controls"
        subtitle="Where each company's workspace is running, and a way to restart it."
        actions={
          <MDButton variant="outlined" color="info" onClick={fetchData} startIcon={<Icon>refresh</Icon>}>
            Refresh
          </MDButton>
        }
      />

      <Section flush>
        {loading && <StateBlock kind="loading" title="Loading" />}
        {error && <StateBlock kind="error" title="The list could not be loaded" message={error} />}
        {!loading && !error && (
          <SimpleTable
            rows={tenants ?? []}
            getRowId={(t: TenantRuntime) => t.id}
            emptyMessage="No companies yet."
            columns={[
              {
                key: "company",
                header: "Company",
                render: (t: TenantRuntime) => <Identity name={t.name} secondary={t.slug} square />,
              },
              {
                key: "status",
                header: "Status",
                render: (t: TenantRuntime) => (
                  <StatusPill tone={TENANT_STATUS_TONE[t.status]} label={TENANT_STATUS_LABELS[t.status]} pulse={t.status === 0} />
                ),
              },
              { key: "port", header: "Port", render: (t: TenantRuntime) => t.port ?? none },
              {
                key: "process",
                header: "Process",
                render: (t: TenantRuntime) =>
                  t.processId ? (
                    <Box>
                      <Box sx={mono}>PID {t.processId}</Box>
                      {t.processStartTimeUtc && (
                        <Box sx={{ fontSize: "0.75rem", color: c.muted }} title={formatDateTime(t.processStartTimeUtc)}>
                          started {timeAgo(t.processStartTimeUtc)}
                        </Box>
                      )}
                    </Box>
                  ) : (
                    none
                  ),
              },
              {
                key: "database",
                header: "Database",
                render: (t: TenantRuntime) => (t.databaseName ? <Box sx={mono}>{t.databaseName}</Box> : none),
              },
              {
                key: "instance",
                header: "Instance ID",
                render: (t: TenantRuntime) =>
                  t.applicationInstanceId ? <Box sx={mono}>{t.applicationInstanceId}</Box> : none,
              },
              {
                key: "actions",
                header: "",
                align: "right",
                render: (t: TenantRuntime) => (
                  <MDButton
                    size="small"
                    variant="outlined"
                    color="info"
                    disabled={t.status !== 1}
                    onClick={() => setRestartTarget(t)}
                  >
                    Restart
                  </MDButton>
                ),
              },
            ]}
          />
        )}
      </Section>

      <ConfirmDialog
        open={!!restartTarget}
        title="Restart workspace"
        message={`Restart ${restartTarget?.name}? Its workspace is briefly unavailable while it restarts.`}
        confirmLabel="Restart"
        confirmColor="info"
        onConfirm={confirmRestart}
        onCancel={() => setRestartTarget(null)}
      />
    </PageShell>
  );
}