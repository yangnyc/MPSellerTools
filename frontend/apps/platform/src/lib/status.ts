import type { KitTone } from "examples/Kit";
import type { ProvisioningJobStatus, TenantStatus } from "../api/types";

// Status → colour tone, shared by every page that shows a company or a job.
export const TENANT_STATUS_TONE: Record<TenantStatus, KitTone> = {
  0: "info",
  1: "success",
  2: "warning",
  3: "error",
};

export const JOB_STATUS_TONE: Record<ProvisioningJobStatus, KitTone> = {
  0: "neutral",
  1: "info",
  2: "success",
  3: "error",
};
