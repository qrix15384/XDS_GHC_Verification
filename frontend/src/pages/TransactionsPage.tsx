import { useEffect, useState } from "react";
import { api, ApiError } from "../api/client";
import { useAuth } from "../auth/AuthContext";
import { Pagination } from "../components/Pagination";
import { exportTransactionsToExcel, exportTransactionsToPdf } from "../utils/export";
import type { Subscriber, TransactionListItem } from "../types/api";

const PAGE_SIZE = 25;
const NO_SUBSCRIBER_FILTER = "";

/** Local <input type="date"> value (start of day) -> ISO UTC instant. */
function dateInputToFromUtc(value: string): string | undefined {
  if (!value) return undefined;
  return new Date(`${value}T00:00:00.000`).toISOString();
}

/** Local <input type="date"> value (end of day, inclusive) -> ISO UTC instant. */
function dateInputToToUtc(value: string): string | undefined {
  if (!value) return undefined;
  return new Date(`${value}T23:59:59.999`).toISOString();
}

export function TransactionsPage() {
  const { session } = useAuth();
  const auth = session!;
  const [items, setItems] = useState<TransactionListItem[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [error, setError] = useState<string | null>(null);
  const [usernameFilter, setUsernameFilter] = useState("");
  const [fromDate, setFromDate] = useState("");
  const [toDate, setToDate] = useState("");
  const [subscribers, setSubscribers] = useState<Subscriber[]>([]);
  const [subscriberFilter, setSubscriberFilter] = useState(NO_SUBSCRIBER_FILTER);
  const [exporting, setExporting] = useState<"excel" | "pdf" | null>(null);

  const filters = {
    username: usernameFilter || undefined,
    fromUtc: dateInputToFromUtc(fromDate),
    toUtc: dateInputToToUtc(toDate),
    subscriberId: subscriberFilter ? Number(subscriberFilter) : undefined,
  };

  useEffect(() => {
    if (auth.role === "Admin") {
      api.listSubscribers(auth).then(setSubscribers).catch(() => setSubscribers([]));
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    let cancelled = false;
    api
      .listTransactions(auth, { page, pageSize: PAGE_SIZE, ...filters })
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
  }, [page, usernameFilter, fromDate, toDate, subscriberFilter]);

  function resetToPageOne() {
    setPage(1);
  }

  async function handleExport(format: "excel" | "pdf") {
    setExporting(format);
    setError(null);
    try {
      const { items: allItems, truncated } = await api.listAllTransactions(auth, filters);
      const filenamePrefix = `xds-ghc-transactions-${new Date().toISOString().slice(0, 10)}`;
      if (format === "excel") {
        await exportTransactionsToExcel(allItems, filenamePrefix);
      } else {
        exportTransactionsToPdf(allItems, filenamePrefix);
      }
      if (truncated) {
        setError(
          "Export was capped at 5000 rows — narrow the date range or username filter to get everything.",
        );
      }
    } catch (err) {
      setError(err instanceof ApiError ? err.detail : "Export failed.");
    } finally {
      setExporting(null);
    }
  }

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
            resetToPageOne();
          }}
        />
        {auth.role === "Admin" && (
          <select
            value={subscriberFilter}
            onChange={(e) => {
              setSubscriberFilter(e.target.value);
              resetToPageOne();
            }}
          >
            <option value={NO_SUBSCRIBER_FILTER}>All subscribers</option>
            {subscribers.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </select>
        )}
        <label className="date-filter">
          From
          <input
            type="date"
            value={fromDate}
            onChange={(e) => {
              setFromDate(e.target.value);
              resetToPageOne();
            }}
          />
        </label>
        <label className="date-filter">
          To
          <input
            type="date"
            value={toDate}
            onChange={(e) => {
              setToDate(e.target.value);
              resetToPageOne();
            }}
          />
        </label>
        {(fromDate || toDate) && (
          <button
            type="button"
            onClick={() => {
              setFromDate("");
              setToDate("");
              resetToPageOne();
            }}
          >
            Clear dates
          </button>
        )}
        <button type="button" disabled={exporting !== null} onClick={() => handleExport("excel")}>
          {exporting === "excel" ? "Exporting…" : "Export Excel"}
        </button>
        <button type="button" disabled={exporting !== null} onClick={() => handleExport("pdf")}>
          {exporting === "pdf" ? "Exporting…" : "Export PDF"}
        </button>
      </div>

      {error && <p className="error">{error}</p>}

      <table>
        <thead>
          <tr>
            <th>Time (UTC)</th>
            <th>Endpoint</th>
            <th>Method</th>
            <th>Username</th>
            <th>Subscriber</th>
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
              <td>{item.subscriberName ?? "—"}</td>
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
