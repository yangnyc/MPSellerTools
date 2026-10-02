import { useCallback, useEffect, useState } from "react";
import Card from "@mui/material/Card";
import Chip from "@mui/material/Chip";
import Dialog from "@mui/material/Dialog";
import DialogTitle from "@mui/material/DialogTitle";
import DialogContent from "@mui/material/DialogContent";
import DialogActions from "@mui/material/DialogActions";
import MDBox from "components/MDBox";
import MDTypography from "components/MDTypography";
import MDButton from "components/MDButton";
import MDInput from "components/MDInput";
import DataTable from "examples/Tables/DataTable";
import DashboardLayout from "examples/LayoutContainers/DashboardLayout";
import WorkspaceNavbar from "../components/WorkspaceNavbar";
import Footer from "examples/Footer";
import { useAuth } from "../auth/useAuth";
import { useSnackbar } from "../components/useSnackbar";
import { ApiError } from "../lib/api";
import { TasksApi, UsersApi } from "../api/resources";
import { TASK_STATUS_LABELS, type TaskStatus, type UserSummary, type WorkItem } from "../api/types";

const NEXT_STATUSES: Record<TaskStatus, TaskStatus[]> = {
  0: [1, 3],
  1: [2, 3],
  2: [],
  3: [],
};

export default function TasksPage() {
  const { user, logout } = useAuth();
  const { notify } = useSnackbar();
  const isTenantAdmin = user?.roles.includes("TenantAdmin") ?? false;

  const [tasks, setTasks] = useState<WorkItem[] | null>(null);
  const [users, setUsers] = useState<UserSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

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

  const load = () => {
    setLoading(true);
    setError(null);
    fetchData();
  };

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

  const changeStatus = async (task: WorkItem, status: TaskStatus) => {
    try {
      await TasksApi.changeStatus(task.id, status, task.rowVersion);
      notify(`Task "${task.title}" moved to ${TASK_STATUS_LABELS[status]}.`, "success");
      load();
    } catch (err) {
      notify(err instanceof ApiError ? err.message : "Status change failed.", "error");
    }
  };

  const userNameById = (id: string) => users.find((u) => u.id === id)?.displayName ?? "Unknown";

  const columns = [
    { Header: "Title", accessor: "title" },
    { Header: "Status", accessor: "status" },
    ...(isTenantAdmin ? [{ Header: "Assigned to", accessor: "assignedTo" }] : []),
    { Header: "Actions", accessor: "actions", align: "right" as const },
  ];

  const rows =
    tasks?.map((t) => ({
      title: t.title,
      status: <Chip size="small" label={TASK_STATUS_LABELS[t.status]} />,
      assignedTo: isTenantAdmin ? userNameById(t.assignedUserId) : undefined,
      actions: (
        <MDBox display="flex" justifyContent="flex-end" gap={1}>
          {NEXT_STATUSES[t.status].map((next) => (
            <MDButton key={next} size="small" variant="outlined" color="info" onClick={() => changeStatus(t, next)}>
              {TASK_STATUS_LABELS[next]}
            </MDButton>
          ))}
        </MDBox>
      ),
    })) ?? [];

  return (
    <DashboardLayout>
      <WorkspaceNavbar onLogout={logout} />
      <MDBox py={3}>
        <Card>
          <MDBox display="flex" justifyContent="space-between" alignItems="center" p={3}>
            <MDTypography variant="h5">Tasks</MDTypography>
            {isTenantAdmin && (
              <MDButton variant="gradient" color="info" onClick={openCreate}>
                Create task
              </MDButton>
            )}
          </MDBox>
          {loading && (
            <MDBox p={3}>
              <MDTypography variant="body2">Loading tasks…</MDTypography>
            </MDBox>
          )}
          {error && (
            <MDBox p={3}>
              <MDTypography variant="body2" color="error">
                {error}
              </MDTypography>
            </MDBox>
          )}
          {!loading && !error && tasks?.length === 0 && (
            <MDBox p={3}>
              <MDTypography variant="body2" color="text">
                {isTenantAdmin ? "No tasks yet." : "No tasks are currently assigned to you."}
              </MDTypography>
            </MDBox>
          )}
          {!loading && !error && (tasks?.length ?? 0) > 0 && <DataTable table={{ columns, rows }} canSearch />}
        </Card>
      </MDBox>
      <Footer />

      <Dialog open={formOpen} onClose={() => setFormOpen(false)} fullWidth maxWidth="sm">
        <DialogTitle>Create task</DialogTitle>
        <DialogContent>
          {formError && (
            <MDBox mb={2}>
              <MDTypography variant="caption" color="error">
                {formError}
              </MDTypography>
            </MDBox>
          )}
          <MDBox mb={2} mt={1}>
            <MDInput label="Title" fullWidth value={title} onChange={(e: React.ChangeEvent<HTMLInputElement>) => setTitle(e.target.value)} />
          </MDBox>
          <MDBox mb={2}>
            <MDInput
              label="Description"
              fullWidth
              multiline
              rows={3}
              value={description}
              onChange={(e: React.ChangeEvent<HTMLInputElement>) => setDescription(e.target.value)}
            />
          </MDBox>
          <MDBox mb={1}>
            <MDInput
              select
              label="Assign to"
              fullWidth
              SelectProps={{ native: true }}
              value={assignedUserId}
              onChange={(e: React.ChangeEvent<HTMLInputElement>) => setAssignedUserId(e.target.value)}
            >
              <option value="" />
              {users
                .filter((u) => !u.isBlocked)
                .map((u) => (
                  <option key={u.id} value={u.id}>
                    {u.displayName} ({u.email})
                  </option>
                ))}
            </MDInput>
          </MDBox>
        </DialogContent>
        <DialogActions>
          <MDButton variant="text" color="secondary" onClick={() => setFormOpen(false)}>
            Cancel
          </MDButton>
          <MDButton variant="gradient" color="info" onClick={submitForm}>
            Create
          </MDButton>
        </DialogActions>
      </Dialog>
    </DashboardLayout>
  );
}
