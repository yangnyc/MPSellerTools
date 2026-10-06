import { InlineAlert } from "examples/Kit";
import type { BulkResult } from "../lib/bulk";

// The outcome of a bulk action: how many went through, and why any didn't.
export default function BulkResultAlert({ result, done }: { result: BulkResult | null; done: string }) {
  if (!result) {
    return null;
  }

  return (
    <InlineAlert tone={result.failures.length > 0 ? "warning" : "success"} sx={{ mb: 2.5 }}>
      {result.succeeded} {done}
      {result.failures.length > 0 && `, ${result.failures.length} failed:`}
      {result.failures.length > 0 && (
        <ul style={{ margin: "0.25rem 0 0", paddingLeft: "1.25rem" }}>
          {result.failures.map((failure) => (
            <li key={failure.label}>
              {failure.label}: {failure.message}
            </li>
          ))}
        </ul>
      )}
    </InlineAlert>
  );
}