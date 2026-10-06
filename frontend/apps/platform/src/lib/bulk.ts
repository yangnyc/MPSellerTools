import { ApiError } from "./api";

export type BulkResult = { succeeded: number; failures: { label: string; message: string }[] };

// Runs one request per item, in order, and keeps going when one fails, so a
// single refusal (say, blocking the last admin) doesn't abandon the rest.
export async function runBulk<T>(
  items: T[],
  labelOf: (item: T) => string,
  action: (item: T) => Promise<unknown>
): Promise<BulkResult> {
  const result: BulkResult = { succeeded: 0, failures: [] };
  for (const item of items) {
    try {
      await action(item);
      result.succeeded += 1;
    } catch (err) {
      result.failures.push({ label: labelOf(item), message: err instanceof ApiError ? err.message : "Request failed." });
    }
  }
  return result;
}