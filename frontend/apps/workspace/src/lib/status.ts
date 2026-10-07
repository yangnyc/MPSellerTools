import type { KitTone } from "examples/Kit";
import type { ListingObservedStatus } from "../api/channels";
import type { ListingStatus, OrderStatus, TaskStatus } from "../api/types";

// What a marketplace itself last reported about a listing; one never sent has its own label.
export const LISTING_OBSERVED: Record<ListingObservedStatus, { label: string; tone: KitTone }> = {
  0: { label: "Not sent", tone: "neutral" },
  1: { label: "Not listed", tone: "neutral" },
  2: { label: "Processing", tone: "warning" },
  3: { label: "Live", tone: "success" },
  4: { label: "Off sale", tone: "neutral" },
  5: { label: "Rejected", tone: "error" },
};

// Status → colour tone, shared by every page that shows an order or a task.
export const ORDER_STATUS_TONE: Record<OrderStatus, KitTone> = {
  0: "info",
  1: "warning",
  2: "success",
  3: "neutral",
};

export const TASK_STATUS_TONE: Record<TaskStatus, KitTone> = {
  0: "info",
  1: "warning",
  2: "success",
  3: "neutral",
};

export const LISTING_STATUS_TONE: Record<ListingStatus, KitTone> = {
  0: "success",
  1: "warning",
  2: "neutral",
};

// The transitions the server permits from each status (orders and tasks share
// the same shape: open → in progress → done, or cancelled from either).
export const NEXT_STATUSES: Record<0 | 1 | 2 | 3, (0 | 1 | 2 | 3)[]> = {
  0: [1, 3],
  1: [2, 3],
  2: [],
  3: [],
};
