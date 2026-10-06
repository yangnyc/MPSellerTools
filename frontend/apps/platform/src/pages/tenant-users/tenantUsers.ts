import { useCallback, useEffect, useState } from "react";
import { ApiError } from "../../lib/api";
import { TenantUsersApi } from "../../api/resources";
import type { TenantUser, UnavailableTenant } from "../../api/types";

export const ROLES = ["TenantAdmin", "Employee"];
export const roleOf = (user: TenantUser) => user.roles[0] ?? "Employee";
// The same user id can exist in two companies, so a row is the pair.
export const rowId = (user: TenantUser) => `${user.tenantId}:${user.id}`;

// Every company's users, read through the platform from each company's own instance.
export function useTenantUsers() {
  const [users, setUsers] = useState<TenantUser[] | null>(null);
  const [unavailable, setUnavailable] = useState<UnavailableTenant[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const reload = useCallback(() => {
    TenantUsersApi.list()
      .then((result) => {
        setUsers(result.users);
        setUnavailable(result.unavailable);
        setError(null);
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load users."))
      .finally(() => setLoading(false));
  }, []);

  useEffect(reload, [reload]);

  return { users, unavailable, error, loading, reload };
}
