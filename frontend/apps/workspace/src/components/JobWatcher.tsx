import { useEffect } from "react";
import { useAuth } from "../auth/useAuth";
import { ApiError } from "../lib/api";
import { BULK_JOB_TYPES, BulkJobsApi, type BulkJob } from "../api/channels";
import { CHANGED, JOB_FINISHED, keep, watched } from "./jobWatch";
import { useSnackbar } from "./useSnackbar";

// What a finished job is said to have come to, and what to do when it did not go well.
function report(job: BulkJob): [string, "success" | "warning" | "error" | "info"] {
  const name = `${BULK_JOB_TYPES[job.type].label}${job.accountName ? ` (${job.accountName})` : ""}`;
  switch (job.status) {
    case 2:
      return [`${name} finished. ${job.summary ?? ""}`.trim(), "success"];
    case 3:
      return [`${name} finished with items held back. ${job.summary ?? ""} Open Jobs to see which, and why.`, "warning"];
    case 5:
      return [`${name} was stopped. ${job.summary ?? ""}`.trim(), "info"];
    default:
      return [`${name} failed. ${job.lastError ?? "It can be run again from Jobs."} Nothing it had not finished was changed; open Jobs to run it again.`, "error"];
  }
}

// Follows the watched jobs from wherever the user is, and says when each is done. It shows nothing itself.
export default function JobWatcher() {
  const { notify } = useSnackbar();
  const { status } = useAuth();

  useEffect(() => {
    if (status !== "authenticated") return undefined;
    let busy = false;

    const check = async () => {
      if (busy) return;
      busy = true;
      try {
        for (const id of watched()) {
          try {
            const job = await BulkJobsApi.get(id);
            if (job.status === 0 || job.status === 1) continue;
            keep(watched().filter((other) => other !== id));
            const [text, severity] = report(job);
            // A job that failed becomes a standing alert, which is where it is kept; said twice it would be counted twice.
            notify(text, severity, { toastOnly: job.status === 4 });
            window.dispatchEvent(new CustomEvent<BulkJob>(JOB_FINISHED, { detail: job }));
          } catch (err) {
            // Removed from the list, or not this user's to see: nothing more will be heard of it.
            if (err instanceof ApiError && (err.status === 404 || err.status === 403 || err.status === 401)) {
              keep(watched().filter((other) => other !== id));
            }
          }
        }
      } finally {
        busy = false;
      }
    };

    check();
    const timer = window.setInterval(check, 3000);
    window.addEventListener(CHANGED, check);
    return () => {
      window.clearInterval(timer);
      window.removeEventListener(CHANGED, check);
    };
  }, [notify, status]);

  return null;
}
