export type Role = "Admin" | "Standard";

export interface LoginResponse {
  apiKey: string;
  tokenType: string;
  token: string;
  role: Role;
  expiresAtUtc: string;
}

export interface ProxyUser {
  id: number;
  username: string;
  role: Role;
  isActive: boolean;
  createdAtUtc: string;
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
}
