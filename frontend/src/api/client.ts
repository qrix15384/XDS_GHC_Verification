import type {
  LoginResponse,
  ProxyUser,
  TransactionDetail,
  TransactionFilters,
  TransactionPageResult,
} from "../types/api";

const BASE_URL = import.meta.env.VITE_API_BASE_URL as string;

export class ApiError extends Error {
  status: number;
  detail: string;

  constructor(status: number, detail: string) {
    super(detail);
    this.status = status;
    this.detail = detail;
  }
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
    const detail =
      (body && typeof body === "object" && "detail" in body && String(body.detail)) ||
      `Request failed with status ${response.status}`;
    throw new ApiError(response.status, detail);
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

  createUser: (auth: AuthContextValue, username: string, password: string, role: string) =>
    request<ProxyUser>("/api/v1/users", {
      method: "POST",
      auth,
      body: JSON.stringify({ username, password, role }),
    }),

  updateUser: (auth: AuthContextValue, id: number, role: string, isActive: boolean) =>
    request<void>(`/api/v1/users/${id}`, {
      method: "PUT",
      auth,
      body: JSON.stringify({ role, isActive }),
    }),

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
