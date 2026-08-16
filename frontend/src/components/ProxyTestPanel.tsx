import { useState } from "react";
import { api, ApiError } from "../api/client";
import { useAuth } from "../auth/AuthContext";

export function ProxyTestPanel() {
  const { session } = useAuth();
  const auth = session!;
  const [method, setMethod] = useState("GET");
  const [path, setPath] = useState("");
  const [body, setBody] = useState("");
  const [result, setResult] = useState<unknown>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function submit() {
    setLoading(true);
    setError(null);
    setResult(null);
    try {
      const response = await api.proxyRequest(auth, method, path, method === "GET" ? undefined : body);
      setResult(response);
    } catch (err) {
      setError(err instanceof ApiError ? err.detail : "Request failed.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="test-panel">
      <h2>Generic Proxy</h2>

      <div className="inline-form">
        <select value={method} onChange={(e) => setMethod(e.target.value)}>
          {["GET", "POST", "PUT", "PATCH", "DELETE"].map((m) => (
            <option key={m} value={m}>
              {m}
            </option>
          ))}
        </select>
        <input
          placeholder="path/under/proxy"
          value={path}
          onChange={(e) => setPath(e.target.value)}
        />
        <button type="button" disabled={loading} onClick={submit}>
          {loading ? "Sending…" : "Send"}
        </button>
      </div>

      {method !== "GET" && (
        <textarea
          placeholder="JSON request body"
          value={body}
          onChange={(e) => setBody(e.target.value)}
          rows={6}
        />
      )}

      {error && <p className="error">{error}</p>}

      {result != null && <pre className="result">{JSON.stringify(result, null, 2)}</pre>}
    </div>
  );
}
