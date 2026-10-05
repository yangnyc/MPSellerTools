import { useCallback, useEffect, useMemo, useState } from "react";
import Box from "@mui/material/Box";
import Icon from "@mui/material/Icon";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import DataTable from "examples/Tables/DataTable";
import {
  FilterTabs,
  Identity,
  InlineAlert,
  KitDialog,
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
import { ApiError } from "../lib/api";
import { TasksApi, UsersApi } from "../api/resources";
import { TASK_STATUS_LABELS, type TaskStatus, type UserSummary, type WorkItem } from "../api/types";
import { NEXT_STATUSES, TASK_STATUS_TONE } from "../lib/status";

type View = "board" | "table";

const TASK_STATUSES: TaskStatus[] = [0, 1, 2, 3];

const NEXT_ACTION_LABELS: Record<TaskStatus, string> = {
  0: "Reopen",
  1: "Start",
  2: "Mark done",
  3: "Cancel task",
};

export default function TasksPage() {
  const { user, logout } = useAuth();
  const { notify } = useSnackbar();
  const kit = useKit();
  const { c } = kit;
  const isTenantAdmin = user?.roles.includes("TenantAdmin") ?? false;

  const [tasks, setTasks] = useState<WorkItem[] | null>(null);
  const [users, setUsers] = useState<UserSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [view, setView] = useState<View>("board");

  const [formOpen, setFormOpen] = useState(false);
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [assignedUserId, setAssignedUserId] = useState("");
  const [formError, setFormError] = useState<string | null>(null);

  const fetchData = useCallback(() => {
    const requests: Promise<unknown>[] = [TasksApi.list().then(setTasks)];
    if (isTenantAdmin) {
      requests.push(UsersApi.list().then(setUsers));
    }
    Promise.all(requests)
      .catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load tasks."))
      .finally(() => setLoading(false));
  }, [isTenantAdmin]);

  useEffect(fetchData, [fetchData]);

  const load = useCallback(() => {
    setLoading(true);
    setError(null);
    fetchData();
  }, [fetchData]);

  const openCreate = () => {
    setTitle("");
    setDescription("");
    setAssignedUserId("");
    setFormError(null);
    setFormOpen(true);
  };

  const submitForm = async () => {
    if (!title.trim()) {
      setFormError("Title is required.");
      return;
    }
    if (!assignedUserId) {
      setFormError("Assign this task to a user.");
      return;
    }
    try {
      await TasksApi.create({ title, description: description || null, assignedUserId, dueAtUtc: null });
      notify("Task created.", "success");
      setFormOpen(false);
      load();
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        await logout();
        return;
      }
      setFormError(err instanceof ApiError ? err.message : "Save failed.");
    }
  };

  const changeStatus = useCallback(
    async (task: WorkItem, status: TaskStatus) => {
      try {
        await TasksApi.changeStatus(task.id, status, task.rowVersion);
        notify(`Task "${task.title}" moved to ${TASK_STATUS_LABELS[status]}.`, "success");
        load();
      } catch (err) {
        notify(err instanceof ApiError ? err.message : "Status change failed.", "error");
      }
    },
    [notify, load]
  );

  const assigneeName = useCallback(
    (id: string) => users.find((u) => u.id === id)?.displayName ?? "Unknown",
    [users]
  );

  const statusActions = useCallback(
    (task: WorkItem) =>
      NEXT_STATUSES[task.status].map((next) => (
        <MDButton
          key={next}
          size="small"
          variant="outlined"
          color={next === 3 ? "secondary" : "info"}
          onClick={() => changeStatus(task, next as TaskStatus)}
        >
          {NEXT_ACTION_LABELS[next as TaskStatus]}
        </MDButton>
      )),
    [changeStatus]
  );

  // Plain values in the rows (so the table's search and sorting work on them)
  // and the presentation in each column's Cell. Memoised because the table
  // resets its page and filter whenever it is handed new row objects.
  const table = useMemo(() => {
    type Row = { task: WorkItem; title: string; statusLabel: string; assignedTo: string; updatedAtUtc: string };
    type CellProps = { row: { original: Row } };

    const rows: Row[] = (tasks ?? []).map((t) => ({
      task: t,
      title: t.title,
      statusLabel: TASK_STATUS_LABELS[t.status],
      assignedTo: assigneeName(t.assignedUserId),
      updatedAtUtc: t.updatedAtUtc,
    }));

    const columns = [
      {
        Header: "Task",
        accessor: "title",
        Cell: ({ row }: CellProps) => (
          <Box sx={{ maxWidth: 420, lineHeight: 1.35, whiteSpace: "normal" }}>
            <Box sx={{ fontWeight: 500, color: c.text }}>{row.original.title}</Box>
            {row.original.task.description && (
              <Box sx={{ fontSize: "0.75rem", color: c.muted }}>{row.original.task.description}</Box>
            )}
          </Box>
        ),
      },
      {
        Header: "Status",
        accessor: "statusLabel",
        Cell: ({ row }: CellProps) => (
          <StatusPill tone={TASK_STATUS_TONE[row.original.task.status]} label={row.original.statusLabel} />
        ),
      },
      ...(isTenantAdmin
        ? [
            {
              Header: "Assigned to",
              accessor: "assignedTo",
              Cell: ({ value }: { value: string }) => <Identity name={value} size={28} />,
            },
          ]
        : []),
      {
        Header: "Updated",
        accessor: "updatedAtUtc",
        Cell: ({ value }: { value: string }) => <span title={formatDateTime(value)}>{timeAgo(value)}</span>,
      },
      {
        Header: "Actions",
        id: "actions",
        accessor: "title",
        align: "right" as const,
        disableSortBy: true,
        disableGlobalFilter: true,
        Cell: ({ row }: CellProps) => (
          <Box sx={{ display: "flex", justifyContent: "flex-end", gap: 1 }}>{statusActions(row.original.task)}</Box>
        ),
      },
    ];
    return { columns, rows };
  }, [tasks, isTenantAdmin, assigneeName, statusActions, c]);

  const hasTasks = !loading && !error && (tasks?.length ?? 0) > 0;

  return (
    <PageShell>
      <PageHeader
        icon="checklist"
        title="Tasks"
        subtitle={
          isTenantAdmin
            ? "Assign work to your team and follow it to completion."
            : "Your assigned work. Update a task's status as you make progress."
        }
        actions={
          <>
            <FilterTabs
              label="View"
              value={view}
              onChange={setView}
              options={[
                { value: "board", label: "Board" },
                { value: "table", label: "Table" },
              ]}
            />
            {isTenantAdmin && (
              <MDButton variant="gradient" color="info" onClick={openCreate} startIcon={<Icon>add</Icon>}>
                Create task
              </MDButton>
            )}
          </>
        }
      />

      {!hasTasks && (
        <Section flush>
          {loading && <StateBlock kind="loading" title="Loading tasks" />}
          {error && (
            <StateBlock
              kind="error"
              title="Tasks could not be loaded"
              message={error}
              action={
                <MDButton variant="outlined" color="info" size="small" onClick={load}>
                  Try again
                </MDButton>
              }
            />
          )}
          {!loading && !error && (
            <StateBlock
              icon="task_alt"
              title="No tasks yet"
              message={isTenantAdmin ? "Create a task and assign it to someone on your team." : "No tasks are currently assigned to you."}
              action={
                isTenantAdmin && (
                  <MDButton variant="gradient" color="info" size="small" onClick={openCreate}>
                    Create task
                  </MDButton>
                )
              }
            />
          )}
        </Section>
      )}

      {hasTasks && view === "table" && (
        <Section flush>
          <DataTable table={table} canSearch />
        </Section>
      )}

      {hasTasks && view === "board" && (
        <Box
          sx={{
            display: "grid",
            gridTemplateColumns: { xs: "1fr", md: "repeat(2, minmax(0, 1fr))", xxl: "repeat(4, minmax(0, 1fr))" },
            alignItems: "start",
            gap: 3,
          }}
        >
          {TASK_STATUSES.map((status) => {
            const column = tasks?.filter((t) => t.status === status) ?? [];
            return (
              <Box
                key={status}
                component="section"
                aria-label={TASK_STATUS_LABELS[status]}
                sx={{
                  p: 1.5,
                  borderRadius: "16px",
                  backgroundColor: c.surfaceAlt,
                  border: `1px solid ${c.border}`,
                  borderTop: `3px solid ${kit.tone(TASK_STATUS_TONE[status]).solid}`,
                }}
              >
                <Box sx={{ display: "flex", alignItems: "center", justifyContent: "space-between", px: 1, pt: 0.5, pb: 1.5 }}>
                  <Box component="h2" sx={{ fontSize: "0.875rem", fontWeight: 700, color: c.text }}>
                    {TASK_STATUS_LABELS[status]}
                  </Box>
                  <Box sx={{ fontSize: "0.75rem", fontWeight: 500, color: c.muted }}>{column.length}</Box>
                </Box>
                {column.length === 0 && (
                  <Box sx={{ px: 1, py: 3, textAlign: "center", fontSize: "0.8125rem", color: c.subtle }}>
                    Nothing here
                  </Box>
                )}
                {column.map((task) => (
                  <Surface key={task.id} sx={{ p: 2, mb: 1.5, "&:last-of-type": { mb: 0 } }}>
                    <Box sx={{ fontSize: "0.875rem", fontWeight: 500, lineHeight: 1.4, color: c.text }}>{task.title}</Box>
                    {task.description && (
                      <Box
                        sx={{
                          mt: 0.5,
                          display: "-webkit-box",
                          WebkitLineClamp: 3,
                          WebkitBoxOrient: "vertical",
                          overflow: "hidden",
                          fontSize: "0.8125rem",
                          lineHeight: 1.5,
                          color: c.muted,
                        }}
                      >
                        {task.description}
                      </Box>
                    )}
                    <Box
                      sx={{
                        display: "flex",
                        flexWrap: "wrap",
                        alignItems: "center",
                        justifyContent: "space-between",
                        gap: 1,
                        mt: 1.5,
                      }}
                    >
                      {isTenantAdmin ? (
                        <Identity name={assigneeName(task.assignedUserId)} size={24} />
                      ) : (
                        <span />
                      )}
                      <Box
                        sx={{ fontSize: "0.75rem", color: c.subtle }}
                        title={formatDateTime(task.dueAtUtc ?? task.updatedAtUtc)}
                      >
                        {task.dueAtUtc ? `Due ${formatDateTime(task.dueAtUtc)}` : `Updated ${timeAgo(task.updatedAtUtc)}`}
                      </Box>
                    </Box>
                    {NEXT_STATUSES[task.status].length > 0 && (
                      <Box sx={{ display: "flex", flexWrap: "wrap", gap: 1, mt: 1.5, pt: 1.5, borderTop: `1px solid ${c.border}` }}>
                        {statusActions(task)}
                      </Box>
                    )}
                  </Surface>
                ))}
              </Box>
            );
          })}
        </Box>
      )}

      <KitDialog
        open={formOpen}
        onClose={() => setFormOpen(false)}
        onSubmit={submitForm}
        icon="add_task"
        title="Create task"
        subtitle="The assignee sees it on their dashboard right away."
        actions={
          <>
            <MDButton variant="text" color="secondary" onClick={() => setFormOpen(false)}>
              Cancel
            </MDButton>
            <MDButton type="submit" variant="gradient" color="info">
              Create
            </MDButton>
          </>
        }
      >
        {formError && <InlineAlert sx={{ mb: 2.5 }}>{formError}</InlineAlert>}
        <Box sx={{ display: "grid", gap: 2.5, pt: 0.5 }}>
          <MDInput
            label="Title"
            fullWidth
            value={title}
            onChange={(e: React.ChangeEvent<HTMLInputElement>) => setTitle(e.target.value)}
          />
          <MDInput
            label="Description"
            fullWidth
            multiline
            rows={3}
            value={description}
            onChange={(e: React.ChangeEvent<HTMLInputElement>) => setDescription(e.target.value)}
          />
          <MDInput
            select
            label="Assign to"
            fullWidth
            SelectProps={{ native: true }}
            InputLabelProps={{ shrink: true }}
            value={assignedUserId}
            onChange={(e: React.ChangeEvent<HTMLInputElement>) => setAssignedUserId(e.target.value)}
          >
            <option value="">Select a team member</option>
            {users
              .filter((u) => !u.isBlocked)
              .map((u) => (
                <option key={u.id} value={u.id}>
                  {u.displayName} ({u.email})
                </option>
              ))}
          </MDInput>
        </Box>
      </KitDialog>
    </PageShell>
  );
}
