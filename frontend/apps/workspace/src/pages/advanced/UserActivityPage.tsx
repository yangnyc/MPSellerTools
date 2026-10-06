import { useEffect, useMemo, useState } from "react";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import DataTable from "examples/Tables/DataTable";
import {
  Identity,
  PageHeader,
  Section,
  SimpleTable,
  StateBlock,
  StatusPill,
  downloadCsv,
  formatDateTime,
  roleLabel,
  timeAgo,
  useKit,
} from "examples/Kit";
import PageShell from "../../components/PageShell";
import { ApiError } from "../../lib/api";
import { AuditApi, UsersApi } from "../../api/resources";
import type { AuditEntry, UserSummary } from "../../api/types";

type ChangeEvent = React.ChangeEvent<HTMLInputElement>;
type Involvement = "any" | "actor" | "subject";

// The most audit entries the server hands out in one request.
const AUDIT_LIMIT = 500;

// The account an entry is about: user management actions record it in their
// details as "user=<email>" or "email=<email>".
const subjectOf = (entry: AuditEntry) =>
  /(?:^|; )(?:user|email)=([^;]+)/.exec(entry.details ?? "")?.[1].trim().toLowerCase() ?? null;

export default function UserActivityPage() {
  const { c } = useKit();
  const [users, setUsers] = useState<UserSummary[] | null>(null);
  const [entries, setEntries] = useState<AuditEntry[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [email, setEmail] = useState("");
  const [involvement, setInvolvement] = useState<Involvement>("any");

  useEffect(() => {
    Promise.all([UsersApi.list(), AuditApi.list(AUDIT_LIMIT)])
      .then(([userList, entryList]) => {
        setUsers(userList);
        setEntries(entryList);
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load activity."))
      .finally(() => setLoading(false));
  }, []);

  // Entries arrive newest first, so the first one seen for a user is their latest.
  const summary = useMemo(() => {
    const byActor = new Map<string, { count: number; last: string }>();
    for (const entry of entries ?? []) {
      const key = entry.actorEmail.toLowerCase();
      const seen = byActor.get(key);
      byActor.set(key, { count: (seen?.count ?? 0) + 1, last: seen?.last ?? entry.occurredAtUtc });
    }
    return byActor;
  }, [entries]);

  const filtered = useMemo(() => {
    const target = email.toLowerCase();
    return (entries ?? []).filter((entry) => {
      const isActor = entry.actorEmail.toLowerCase() === target;
      const isSubject = subjectOf(entry) === target;
      if (!target) return involvement !== "subject" || subjectOf(entry) !== null;
      return involvement === "actor" ? isActor : involvement === "subject" ? isSubject : isActor || isSubject;
    });
  }, [entries, email, involvement]);

  // Plain values in the rows (so the table's search and sorting work on them)
  // and the presentation in each column's Cell. Memoised because the table
  // resets its page and filter whenever it is handed new row objects.
  const table = useMemo(() => {
    const columns = [
      {
        Header: "When",
        accessor: "occurredAtUtc",
        Cell: ({ value }: { value: string }) => (
          <Box sx={{ lineHeight: 1.35 }}>
            <Box sx={{ fontWeight: 500, color: c.text }}>{timeAgo(value)}</Box>
            <Box sx={{ fontSize: "0.75rem", color: c.muted }}>{formatDateTime(value)}</Box>
          </Box>
        ),
      },
      { Header: "Done by", accessor: "actorEmail" },
      {
        Header: "Action",
        accessor: "action",
        Cell: ({ value }: { value: string }) => <StatusPill tone="primary" label={value} />,
      },
      {
        Header: "Details",
        accessor: (entry: AuditEntry) => entry.details ?? "",
        id: "details",
        Cell: ({ value }: { value: string }) => (
          <Box sx={{ maxWidth: 480, whiteSpace: "normal", overflowWrap: "anywhere" }}>{value || "—"}</Box>
        ),
      },
    ];
    return { columns, rows: filtered };
  }, [filtered, c]);

  const isFiltered = email !== "" || involvement !== "any";
  const reset = () => {
    setEmail("");
    setInvolvement("any");
  };

  const exportCsv = () =>
    downloadCsv(
      "user-activity.csv",
      ["When (UTC)", "Done by", "Action", "Details"],
      filtered.map((entry) => [entry.occurredAtUtc, entry.actorEmail, entry.action, entry.details])
    );

  return (
    <PageShell>
      <PageHeader
        icon="timeline"
        title="User activity"
        subtitle="What each user has done in this workspace, and what has been done to their account."
        actions={
          <MDButton
            variant="gradient"
            color="info"
            disabled={filtered.length === 0}
            onClick={exportCsv}
            startIcon={<Icon>file_download</Icon>}
          >
            Export CSV ({filtered.length})
          </MDButton>
        }
      />

      {loading && (
        <Section>
          <StateBlock kind="loading" title="Loading" />
        </Section>
      )}
      {error && (
        <Section>
          <StateBlock kind="error" title="Activity could not be loaded" message={error} />
        </Section>
      )}

      {!loading && !error && (
        <Box sx={{ display: "grid", gap: 3 }}>
          <Section
            flush
            icon="group"
            title="Activity by user"
            subtitle={
              entries?.length === AUDIT_LIMIT
                ? `Counted over the latest ${AUDIT_LIMIT} audit events; older ones are not included.`
                : "Counted over every recorded audit event."
            }
          >
            <SimpleTable
              rows={users ?? []}
              getRowId={(u: UserSummary) => u.id}
              emptyMessage="No users yet."
              columns={[
                { key: "user", header: "User", render: (u: UserSummary) => <Identity name={u.displayName} secondary={u.email} /> },
                { key: "role", header: "Role", render: (u: UserSummary) => roleLabel(u.roles[0] ?? "Employee") },
                {
                  key: "events",
                  header: "Actions taken",
                  align: "right",
                  render: (u: UserSummary) => summary.get(u.email.toLowerCase())?.count ?? 0,
                },
                {
                  key: "last",
                  header: "Last action",
                  render: (u: UserSummary) => {
                    const last = summary.get(u.email.toLowerCase())?.last;
                    return last ? <span title={formatDateTime(last)}>{timeAgo(last)}</span> : "—";
                  },
                },
                {
                  key: "actions",
                  header: "",
                  align: "right",
                  render: (u: UserSummary) => (
                    <MDButton
                      size="small"
                      variant={u.email === email ? "gradient" : "outlined"}
                      color="info"
                      aria-label={`Show activity for ${u.displayName}`}
                      aria-pressed={u.email === email}
                      onClick={() => setEmail(u.email === email ? "" : u.email)}
                    >
                      {u.email === email ? "Showing" : "Show"}
                    </MDButton>
                  ),
                },
              ]}
            />
          </Section>

          <Section
            flush
            icon="history"
            title={`${filtered.length} of ${entries?.length ?? 0} events`}
            actions={
              <MDButton size="small" variant="text" color="secondary" disabled={!isFiltered} onClick={reset}>
                Clear filters
              </MDButton>
            }
          >
            <Box sx={{ display: "grid", gap: 2, px: 3, py: 2.5, gridTemplateColumns: { xs: "1fr", md: "1fr 1fr" } }}>
              <MDInput
                select
                label="User"
                size="small"
                SelectProps={{ native: true }}
                value={email}
                onChange={(e: ChangeEvent) => setEmail(e.target.value)}
              >
                <option value="">Any user</option>
                {(users ?? []).map((u) => (
                  <option key={u.id} value={u.email}>
                    {u.displayName} ({u.email})
                  </option>
                ))}
              </MDInput>
              <MDInput
                select
                label="Involvement"
                size="small"
                SelectProps={{ native: true }}
                value={involvement}
                onChange={(e: ChangeEvent) => setInvolvement(e.target.value as Involvement)}
              >
                <option value="any">Any</option>
                <option value="actor">Actions they took</option>
                <option value="subject">Changes to their account</option>
              </MDInput>
            </Box>
            {filtered.length === 0 ? (
              <StateBlock icon="history" title="No matching activity" message="Nothing recorded matches these filters." />
            ) : (
              <DataTable table={table} canSearch />
            )}
          </Section>
        </Box>
      )}
    </PageShell>
  );
}
