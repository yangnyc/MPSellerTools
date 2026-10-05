import { useEffect, useState } from "react";
import { Link as RouterLink } from "react-router-dom";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import MDButton from "components/MDButton";
import {
  Hero,
  Identity,
  InlineAlert,
  Section,
  StatCard,
  StateBlock,
  StatusPill,
  formatDateTime,
  timeAgo,
  useKit,
} from "examples/Kit";
import PageShell from "../components/PageShell";
import { useAuth } from "../auth/useAuth";
import { DashboardApi } from "../api/resources";
import {
  JOB_STATUS_LABELS,
  JOB_TYPE_LABELS,
  TENANT_STATUS_LABELS,
  type PlatformDashboard,
  type TenantStatus,
  type TenantStatusCounts,
} from "../api/types";
import { JOB_STATUS_TONE, TENANT_STATUS_TONE } from "../lib/status";

const STATES: { status: TenantStatus; key: keyof TenantStatusCounts; icon: string; hint: string }[] = [
  { status: 1, key: "active", icon: "check_circle", hint: "Running and reachable" },
  { status: 0, key: "provisioning", icon: "hourglass_top", hint: "Being set up" },
  { status: 2, key: "suspended", icon: "pause_circle", hint: "Access switched off" },
  { status: 3, key: "failed", icon: "error", hint: "Need attention" },
];

export default function DashboardPage() {
  const { user } = useAuth();
  const kit = useKit();
  const { c } = kit;
  const [data, setData] = useState<PlatformDashboard | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    DashboardApi.get()
      .then(setData)
      .catch(() => setFailed(true));
  }, []);

  const firstName = (user?.displayName || user?.email || "").split(/[\s@]/)[0];
  const counts = data?.tenantCounts;
  const total = counts ? counts.active + counts.provisioning + counts.suspended + counts.failed : 0;

  return (
    <PageShell>
      <Hero
        eyebrow="Platform Dashboard"
        title={firstName ? `Welcome back, ${firstName}` : "Welcome back"}
        subtitle="Tenant counts, instance states, and recent provisioning jobs."
        actions={
          <>
            <MDButton component={RouterLink} to="/tenants/new" color="white" startIcon={<Icon>add</Icon>}>
              Create company
            </MDButton>
            <MDButton component={RouterLink} to="/tenants" variant="outlined" color="white" startIcon={<Icon>apartment</Icon>}>
              All tenants
            </MDButton>
          </>
        }
      />

      {failed && (
        <InlineAlert sx={{ mb: 3 }}>The dashboard could not be loaded. Reload the page to try again.</InlineAlert>
      )}

      <Box sx={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: 3, mb: 3 }}>
        {STATES.map((state) => (
          <StatCard
            key={state.key}
            icon={state.icon}
            tone={TENANT_STATUS_TONE[state.status]}
            label={TENANT_STATUS_LABELS[state.status]}
            value={counts?.[state.key]}
            hint={state.hint}
            progress={counts && total > 0 ? counts[state.key] / total : undefined}
            to="/tenants"
          />
        ))}
      </Box>

      <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", lg: "2fr 3fr" }, alignItems: "start", gap: 3 }}>
        <Section icon="donut_small" tone="primary" title="Instance states" subtitle={counts ? `${total} in total` : undefined}>
          {!counts && !failed && <StateBlock kind="loading" title="Loading instance states" />}
          {counts && total === 0 && (
            <StateBlock
              icon="apartment"
              title="Nothing provisioned yet"
              message="Create the first company to see its instance here."
            />
          )}
          {counts && total > 0 && (
            <>
              <Box
                role="img"
                aria-label={STATES.map((s) => `${counts[s.key]} ${TENANT_STATUS_LABELS[s.status].toLowerCase()}`).join(", ")}
                sx={{ display: "flex", gap: "3px", height: 12, mb: 3, borderRadius: "6px", overflow: "hidden" }}
              >
                {STATES.filter((s) => counts[s.key] > 0).map((s) => (
                  <Box
                    key={s.key}
                    sx={{ flexGrow: counts[s.key], flexBasis: 0, backgroundColor: kit.tone(TENANT_STATUS_TONE[s.status]).solid }}
                  />
                ))}
              </Box>
              {STATES.map((s) => (
                <Box
                  key={s.key}
                  sx={{
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "space-between",
                    py: 1,
                    borderTop: `1px solid ${c.border}`,
                  }}
                >
                  <StatusPill tone={TENANT_STATUS_TONE[s.status]} label={TENANT_STATUS_LABELS[s.status]} />
                  <Box sx={{ fontSize: "0.875rem", color: c.muted }}>
                    <Box component="span" sx={{ mr: 1, fontWeight: 700, color: c.text }}>
                      {counts[s.key]}
                    </Box>
                    {Math.round((counts[s.key] / total) * 100)}%
                  </Box>
                </Box>
              ))}
            </>
          )}
        </Section>

        <Section
          icon="work_history"
          title="Recent provisioning jobs"
          subtitle="Latest activity from the provisioning worker"
          actions={
            <MDButton component={RouterLink} to="/jobs" variant="text" color="info" size="small">
              View all
            </MDButton>
          }
          flush
        >
          {!data && !failed && <StateBlock kind="loading" title="Loading jobs" />}
          {data?.recentJobs.length === 0 && (
            <StateBlock icon="work_history" title="No provisioning activity yet" message="Jobs appear here as soon as a company is created." />
          )}
          {data?.recentJobs.map((job) => (
            <Box
              key={job.id}
              sx={{
                display: "flex",
                alignItems: "center",
                justifyContent: "space-between",
                gap: 2,
                px: 3,
                py: 1.75,
                borderBottom: `1px solid ${c.border}`,
                "&:last-of-type": { borderBottom: "none" },
              }}
            >
              <Identity name={job.tenantName} secondary={`${JOB_TYPE_LABELS[job.jobType]} job`} square />
              <Box sx={{ display: "flex", alignItems: "center", gap: 2 }}>
                <Box
                  sx={{ display: { xs: "none", sm: "block" }, fontSize: "0.75rem", color: c.muted }}
                  title={formatDateTime(job.updatedAtUtc)}
                >
                  {timeAgo(job.updatedAtUtc)}
                </Box>
                <StatusPill
                  tone={JOB_STATUS_TONE[job.status]}
                  label={JOB_STATUS_LABELS[job.status]}
                  pulse={job.status === 1}
                />
              </Box>
            </Box>
          ))}
        </Section>
      </Box>
    </PageShell>
  );
}
