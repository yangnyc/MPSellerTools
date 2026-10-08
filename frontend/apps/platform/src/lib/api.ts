// Minimal cookie-session + antiforgery-aware fetch client (brief §7). No
// bearer tokens, nothing in localStorage — the browser handles the session
// cookie automatically via `credentials: "include"`.

let cachedAntiforgeryToken: string | null = null;

async function getAntiforgeryToken(): Promise<string> {
  if (cachedAntiforgeryToken) {
    return cachedAntiforgeryToken;
  }
  const response = await fetch("/api/antiforgery/token", { credentials: "include" });
  const data = (await response.json()) as { token: string };
  cachedAntiforgeryToken = data.token;
  return cachedAntiforgeryToken;
}

// The server binds each token to the identity it was issued for, so a token
// fetched while anonymous is rejected once signed in (and vice versa).
export function resetAntiforgeryToken() {
  cachedAntiforgeryToken = null;
}

export class ApiError extends Error {
  status: number;

  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

const SAFE_METHODS = new Set(["GET", "HEAD", "OPTIONS", "TRACE"]);

// How many requests are under way, for the loading bar (components/LoadingBar.tsx).
let pending = 0;
const pendingListeners = new Set<() => void>();
const changePending = (by: number) => {
  pending += by;
  pendingListeners.forEach((listener) => listener());
};
export const pendingRequests = () => pending;
export const subscribeToPendingRequests = (listener: () => void) => {
  pendingListeners.add(listener);
  return () => {
    pendingListeners.delete(listener);
  };
};

export async function apiFetch<T>(path: string, init: RequestInit = {}): Promise<T> {
  changePending(1);
  try {
    return await send<T>(path, init);
  } finally {
    changePending(-1);
  }
}

async function send<T>(path: string, init: RequestInit): Promise<T> {
  const method = (init.method ?? "GET").toUpperCase();
  const headers = new Headers(init.headers);

  if (!SAFE_METHODS.has(method)) {
    headers.set("X-CSRF-TOKEN", await getAntiforgeryToken());
  }
  if (init.body && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }

  const response = await fetch(path, { ...init, method, headers, credentials: "include" });

  if (response.status === 401) {
    throw new ApiError(401, "Not authenticated");
  }

  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    // The detail is the server's own explanation; the title is only the status name ("Bad Request").
    throw new ApiError(response.status, problem?.detail ?? problem?.title ?? "Request failed");
  }

  // 204 No Content, and 202 Accepted from the endpoints that only queue work,
  // carry no body to parse.
  const body = await response.text();
  return (body ? JSON.parse(body) : undefined) as T;
}
