// The background jobs this browser tab started and is waiting to hear the end of. Kept for the tab, so a
// reload does not lose them.
const WATCHED = "mpst.watched-jobs";
export const CHANGED = "mpst-watched-jobs";

/** Sent on `window` when a watched job has finished, with the job as its detail, for a page that shows what the job changed. */
export const JOB_FINISHED = "mpst-job-finished";

export const watched = (): string[] => {
  try {
    const ids = JSON.parse(window.sessionStorage.getItem(WATCHED) ?? "[]");
    return Array.isArray(ids) ? ids.filter((id) => typeof id === "string") : [];
  } catch {
    return [];
  }
};

export const keep = (ids: string[]) => {
  try {
    window.sessionStorage.setItem(WATCHED, JSON.stringify(ids));
  } catch {
    // Not kept past a reload; the Jobs page still has the job.
  }
};

/** Has the user told, with a notification, when this job finishes, whichever page they are on by then. */
export function watchJob(id: string) {
  keep([...new Set([...watched(), id])]);
  window.dispatchEvent(new Event(CHANGED));
}

export const isJobWatched = (id: string) => watched().includes(id);
