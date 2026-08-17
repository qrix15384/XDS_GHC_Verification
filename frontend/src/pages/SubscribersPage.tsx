import { useEffect, useState } from "react";
import { api, ApiError } from "../api/client";
import { useAuth } from "../auth/AuthContext";
import type { Subscriber } from "../types/api";

export function SubscribersPage() {
  const { session } = useAuth();
  const auth = session!;
  const [subscribers, setSubscribers] = useState<Subscriber[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api
      .listSubscribers(auth)
      .then(setSubscribers)
      .catch((err) => setError(err instanceof ApiError ? err.detail : "Failed to load subscribers."));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <div className="page">
      <h1>Subscribers</h1>
      <p className="hint">
        Client organizations (e.g. banks) using this service. This list is read-only here — it's
        sourced live from the system of record and managed there, not in this console. Assign a
        proxy user account to one from the Users tab so their calls are attributed to it.
      </p>

      {error && <p className="error">{error}</p>}

      <table>
        <thead>
          <tr>
            <th>Name</th>
            <th>Active</th>
          </tr>
        </thead>
        <tbody>
          {subscribers.map((subscriber) => (
            <tr key={subscriber.id}>
              <td>{subscriber.name}</td>
              <td>{subscriber.isActive ? "Yes" : "No"}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
