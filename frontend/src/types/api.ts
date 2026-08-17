export type Role = "Admin" | "Standard";

export interface LoginResponse {
  apiKey: string;
  tokenType: string;
  token: string;
  role: Role;
  expiresAtUtc: string;
}

export interface Subscriber {
  id: number;
  name: string;
  isActive: boolean;
  createdAtUtc: string;
}

export interface ProxyUser {
  id: number;
  username: string;
  role: Role;
  isActive: boolean;
  createdAtUtc: string;
  subscriberId: number | null;
  subscriberName: string | null;
}

export interface TransactionListItem {
  id: number;
  requestId: string;
  requestAtUtc: string;
  endpointPath: string;
  httpMethod: string;
  username: string | null;
  httpStatusCode: number;
  detailsFound: string | null;
  errorMessage: string | null;
  durationMs: number | null;
  pinNumber: string | null;
  subscriberId: number | null;
  subscriberName: string | null;
}

export interface TransactionDetail extends TransactionListItem {
  responsePayload: unknown;
  rawResponsePayload: string | null;
}

export interface TransactionPageResult {
  items: TransactionListItem[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface TransactionFilters {
  page?: number;
  pageSize?: number;
  username?: string;
  endpointPath?: string;
  httpStatusCode?: number;
  detailsFound?: string;
  /** ISO 8601 — inclusive start of range (UTC). */
  fromUtc?: string;
  /** ISO 8601 — inclusive end of range (UTC). */
  toUtc?: string;
  subscriberId?: number;
}
