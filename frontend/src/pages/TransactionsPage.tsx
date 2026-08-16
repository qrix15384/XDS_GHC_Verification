import { useEffect, useState } from "react";
import { api, ApiError } from "../api/client";
import { useAuth } from "../auth/AuthContext";
import { Pagination } from "../components/Pagination";
import type { TransactionListItem } from "../types/api";

const PAGE_SIZE = 25;

export function TransactionsPage() {
  const { session } = useAuth();
  const auth = session!;
  const [items, setItems] = useState<TransactionListItem[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [error, setError] = useState<string | null>(null);
  const [usernameFilter, setUsernameFilter] = useState("");

  useEffect(() => {
    let cancelled = false;
    api
      .listTransactions(auth, { page, pageSize: PAGE_SIZE, username: usernameFilter || undefined })
      .then((result) => {
        if (cancelled) return;
        setItems(result.items);
        setTotalCount(result.totalCount);
        setError(null);
      })
      .catch((err) => {
        if (cancelled) return;
        setError(err instanceof ApiError ? err.detail : "Failed to load transactions.");
      });
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page, usernameFilter]);

  return (
    <div className="page">
      <h1>Transactions</h1>
      {auth.role !== "Admin" && (
        <p className="hint">Ghana Card PINs are hidden for Standard-role accounts.</p>
      )}

      <div className="inline-form">
        <input
          placeholder="Filter by username"
          value={usernameFilter}
          onChange={(e) => {
            setUsernameFilter(e.target.value);
            setPage(1);
          }}
        />
      </div>

      {error && <p className="error">{error}</p>}

      <table>
        <thead>
          <tr>
            <th>Time (UTC)</th>
            <th>Endpoint</th>
            <th>Method</th>
            <th>Username</th>
            <th>Status</th>
            <th>Found</th>
            <th>PIN</th>
            <th>Duration</th>
          </tr>
        </thead>
        <tbody>
          {items.map((item) => (
            <tr key={item.id}>
              <td>{new Date(item.requestAtUtc).toLocaleString()}</td>
              <td>{item.endpointPath}</td>
              <td>{item.httpMethod}</td>
              <td>{item.username}</td>
              <td>{item.httpStatusCode}</td>
              <td>{item.detailsFound ?? "-"}</td>
              <td>{item.pinNumber ?? "—"}</td>
              <td>{item.durationMs != null ? `${item.durationMs}ms` : "-"}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <Pagination page={page} pageSize={PAGE_SIZE} totalCount={totalCount} onPageChange={setPage} />
    </div>
  );
}
