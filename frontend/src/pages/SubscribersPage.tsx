import { useEffect, useState, type FormEvent } from "react";
import { api, ApiError } from "../api/client";
import { useAuth } from "../auth/AuthContext";
import type { Subscriber } from "../types/api";

export function SubscribersPage() {
  const { session } = useAuth();
  const auth = session!;
  const [subscribers, setSubscribers] = useState<Subscriber[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [newName, setNewName] = useState("");

  async function reload() {
    try {
      setSubscribers(await api.listSubscribers(auth));
    } catch (err) {
      setError(err instanceof ApiError ? err.detail : "Failed to load subscribers.");
    }
  }

  useEffect(() => {
    void reload();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function handleCreate(e: FormEvent) {
    e.preventDefault();
    setError(null);
    try {
      await api.createSubscriber(auth, newName);
      setNewName("");
      await reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.detail : "Failed to create subscriber.");
    }
  }

  async function handleToggleActive(subscriber: Subscriber) {
    setError(null);
    try {
      await api.updateSubscriber(auth, subscriber.id, subscriber.name, !subscriber.isActive);
      await reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.detail : "Failed to update subscriber.");
    }
  }

  async function handleRename(subscriber: Subscriber) {
    const newName = window.prompt("New name:", subscriber.name);
    if (!newName || newName === subscriber.name) return;
    setError(null);
    try {
      await api.updateSubscriber(auth, subscriber.id, newName, subscriber.isActive);
      await reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.detail : "Failed to rename subscriber.");
    }
  }

  async function handleDelete(subscriber: Subscriber) {
    if (!window.confirm(`Delete subscriber "${subscriber.name}"?`)) return;
    setError(null);
    try {
      await api.deleteSubscriber(auth, subscriber.id);
      await reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.detail : "Failed to delete subscriber.");
    }
  }

  return (
    <div className="page">
      <h1>Subscribers</h1>
      <p className="hint">
        Client organizations (e.g. banks, telcos) using this service. Assign proxy user accounts to
        one from the Users tab so every call they make is attributed to it in the audit log.
      </p>

      <form className="inline-form" onSubmit={handleCreate}>
        <input
          placeholder="Subscriber name"
          value={newName}
          onChange={(e) => setNewName(e.target.value)}
          required
        />
        <button type="submit">Create subscriber</button>
      </form>

      {error && <p className="error">{error}</p>}

      <table>
        <thead>
          <tr>
            <th>Name</th>
            <th>Active</th>
            <th>Created</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {subscribers.map((subscriber) => (
            <tr key={subscriber.id}>
              <td>{subscriber.name}</td>
              <td>{subscriber.isActive ? "Yes" : "No"}</td>
              <td>{new Date(subscriber.createdAtUtc).toLocaleString()}</td>
              <td className="actions">
                <button onClick={() => handleRename(subscriber)}>Rename</button>
                <button onClick={() => handleToggleActive(subscriber)}>
                  {subscriber.isActive ? "Deactivate" : "Activate"}
                </button>
                <button className="danger" onClick={() => handleDelete(subscriber)}>
                  Delete
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
