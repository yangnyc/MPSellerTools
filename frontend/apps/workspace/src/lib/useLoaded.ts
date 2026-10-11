import { useCallback, useEffect, useState } from "react";
import { ApiError } from "./api";

// What a page reads once to show: the data, or why it could not be read, and a way to read it again.
export function useLoaded<T>(read: () => Promise<T>, fallback: string) {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const reload = useCallback(
    () =>
      read()
        .then((value) => {
          setData(value);
          setError(null);
        })
        .catch((err) => setError(err instanceof ApiError ? err.message : fallback))
        .finally(() => setLoading(false)),
    [read, fallback]
  );

  useEffect(() => {
    reload();
  }, [reload]);

  return { data, error, loading, reload };
}
