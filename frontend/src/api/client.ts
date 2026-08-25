import type {
  LoginResponse,
  ProxyUser,
  Subscriber,
  TransactionDetail,
  TransactionFilters,
  TransactionListItem,
  TransactionPageResult,
} from "../types/api";

// VITE_API_BASE_URL points at a separate origin for local dev (the API dev
// server runs on its own port). When unset (production), fall back to Vite's
// own BASE_URL so calls stay under whatever sub-path the app is deployed at
// (e.g. "/xdsghc") instead of resolving against the domain root.
const BASE_URL = import.meta.env.VITE_API_BASE_URL || import.meta.env.BASE_URL.replace(/\/$/, "");

export class ApiError extends Error {
  status: number;
  detail: string;

  constructor(status: number, detail: string) {
    super(detail);
    this.status = status;
    this.detail = detail;
  }
}

// The backend's `detail` field is a plain string for validation errors, but
// for relayed upstream (NIA) rejections it's an object shaped like
// {data, success, code, msg} — see SelfieController.VerifyAndLogAsync.
// Pull out the human-readable message in that case instead of collapsing it
// to the literal string "[object Object]".
function extractDetail(body: unknown, status: number): string {
  if (body && typeof body === "object" && "detail" in body) {
    const detail = (body as { detail: unknown }).detail;
    if (typeof detail === "string") return detail;
    if (detail && typeof detail === "object" && "msg" in detail) {
      const msg = (detail as { msg: unknown }).msg;
      if (typeof msg === "string") return msg;
    }
    if (detail !== undefined) return JSON.stringify(detail);
  }
  return `Request failed with status ${status}`;
}

export interface AuthContextValue {
  apiKey: string;
  token: string;
}

async function request<T>(
  path: string,
  options: RequestInit & { auth?: Partial<AuthContextValue> } = {},
): Promise<T> {
  const { auth, headers, ...rest } = options;
  const finalHeaders = new Headers(headers);
  if (auth?.token) {
    finalHeaders.set("Authorization", `Bearer ${auth.token}`);
  }
  if (auth?.apiKey) {
    finalHeaders.set("X-API-Key", auth.apiKey);
  }
  if (rest.body && !finalHeaders.has("Content-Type")) {
    finalHeaders.set("Content-Type", "application/json");
  }

  const response = await fetch(`${BASE_URL}${path}`, { ...rest, headers: finalHeaders });

  if (response.status === 204) {
    return undefined as T;
  }

  const text = await response.text();
  const body = text ? JSON.parse(text) : null;

  if (!response.ok) {
    console.error(`API error ${response.status} for ${path}:`, body);
    throw new ApiError(response.status, extractDetail(body, response.status));
  }

  return body as T;
}

export const api = {
  login: (username: string, password: string) =>
    request<LoginResponse>("/api/v1/auth/login", {
      method: "POST",
      body: JSON.stringify({ username, password }),
    }),

  listUsers: (auth: AuthContextValue) =>
    request<ProxyUser[]>("/api/v1/users", { auth }),

  createUser: (
    auth: AuthContextValue,
    username: string,
    password: string,
    role: string,
    subscriberId: number | null,
  ) =>
    request<ProxyUser>("/api/v1/users", {
      method: "POST",
      auth,
      body: JSON.stringify({ username, password, role, subscriberId }),
    }),

  updateUser: (auth: AuthContextValue, id: number, role: string, isActive: boolean, subscriberId: number | null) =>
    request<void>(`/api/v1/users/${id}`, {
      method: "PUT",
      auth,
      body: JSON.stringify({ role, isActive, subscriberId }),
    }),

  // Read-only — subscribers are managed entirely outside this app.
  listSubscribers: (auth: AuthContextValue) =>
    request<Subscriber[]>("/api/v1/subscribers", { auth }),

  resetPassword: (auth: AuthContextValue, id: number, newPassword: string) =>
    request<void>(`/api/v1/users/${id}/reset-password`, {
      method: "POST",
      auth,
      body: JSON.stringify({ newPassword }),
    }),

  deleteUser: (auth: AuthContextValue, id: number) =>
    request<void>(`/api/v1/users/${id}`, { method: "DELETE", auth }),

  listTransactions: (auth: AuthContextValue, filters: TransactionFilters) => {
    const params = new URLSearchParams();
    for (const [key, value] of Object.entries(filters)) {
      if (value !== undefined && value !== "") {
        params.set(key, String(value));
      }
    }
    return request<TransactionPageResult>(`/api/v1/transactions?${params}`, { auth });
  },

  /**
   * Fetches every row matching the given filters (ignoring `page`), paging
   * through the server's 100-row-per-request cap — used for exports, where
   * "download this date range" should mean the whole range, not one page.
   * Capped at EXPORT_ROW_LIMIT rows as a safety backstop; `truncated` tells
   * the caller whether that cap was actually hit so it isn't silently lossy.
   */
  listAllTransactions: async (
    auth: AuthContextValue,
    filters: Omit<TransactionFilters, "page" | "pageSize">,
  ): Promise<{ items: TransactionListItem[]; truncated: boolean }> => {
    const EXPORT_ROW_LIMIT = 5000;
    const pageSize = 100;
    let page = 1;
    let items: TransactionListItem[] = [];
    let totalCount = Infinity;

    while (items.length < totalCount && items.length < EXPORT_ROW_LIMIT) {
      const result = await api.listTransactions(auth, { ...filters, page, pageSize });
      items = items.concat(result.items);
      totalCount = result.totalCount;
      page += 1;
      if (result.items.length === 0) break;
    }

    return { items: items.slice(0, EXPORT_ROW_LIMIT), truncated: totalCount > EXPORT_ROW_LIMIT };
  },

  getTransaction: (auth: AuthContextValue, id: number) =>
    request<TransactionDetail>(`/api/v1/transactions/${id}`, { auth }),

  selfieVerify: (
    auth: AuthContextValue,
    mode: "kyc" | "yes_no",
    pinNumber: string,
    image: string,
  ) =>
    request<unknown>(`/api/v1/selfie/verification/${mode}/face`, {
      method: "POST",
      auth,
      body: JSON.stringify({ pinNumber, image }),
    }),

  proxyRequest: (auth: AuthContextValue, method: string, path: string, body?: string) =>
    request<unknown>(`/api/v1/proxy/${path}`, {
      method,
      auth,
      body: body || undefined,
    }),
};
