import { useState } from "react";
import { api, ApiError } from "../api/client";
import { useAuth } from "../auth/AuthContext";
import { CameraCapture } from "./CameraCapture";

export function SelfieTestPanel() {
  const { session } = useAuth();
  const auth = session!;
  const [mode, setMode] = useState<"kyc" | "yes_no">("kyc");
  const [pinNumber, setPinNumber] = useState("");
  const [image, setImage] = useState<{ base64: string; sizeBytes: number } | null>(null);
  const [result, setResult] = useState<unknown>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function submit() {
    if (!image) return;
    setLoading(true);
    setError(null);
    setResult(null);
    try {
      const response = await api.selfieVerify(auth, mode, pinNumber, image.base64);
      setResult(response);
    } catch (err) {
      setError(err instanceof ApiError ? err.detail : "Request failed.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="test-panel">
      <h2>Selfie Verification (real NIA upstream)</h2>

      <label>
        Verification type
        <select value={mode} onChange={(e) => setMode(e.target.value as "kyc" | "yes_no")}>
          <option value="kyc">KYC face match</option>
          <option value="yes_no">YES/NO face match</option>
        </select>
      </label>

      <label>
        Ghana Card PIN
        <input
          placeholder="GHA-123456789-0"
          value={pinNumber}
          onChange={(e) => setPinNumber(e.target.value)}
        />
      </label>

      <CameraCapture onCapture={(base64, sizeBytes) => setImage({ base64, sizeBytes })} />

      {image && (
        <button type="button" disabled={!pinNumber || loading} onClick={submit}>
          {loading ? "Sending…" : "Send to NIA"}
        </button>
      )}

      {error && <p className="error">{error}</p>}

      {result != null && (
        <pre className="result">{JSON.stringify(result, null, 2)}</pre>
      )}
    </div>
  );
}
