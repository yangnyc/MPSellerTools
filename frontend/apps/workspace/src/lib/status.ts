import type { KitTone } from "examples/Kit";
import type { OrderStatus, TaskStatus } from "../api/types";

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

// The transitions the server permits from each status (orders and tasks share
// the same shape: open → in progress → done, or cancelled from either).
export const NEXT_STATUSES: Record<0 | 1 | 2 | 3, (0 | 1 | 2 | 3)[]> = {
  0: [1, 3],
  1: [2, 3],
  2: [],
  3: [],
};
