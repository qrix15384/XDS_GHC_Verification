import { useState } from "react";
import { SelfieTestPanel } from "../components/SelfieTestPanel";
import { ProxyTestPanel } from "../components/ProxyTestPanel";

export function TestApiPage() {
  const [tab, setTab] = useState<"selfie" | "proxy">("selfie");

  return (
    <div className="page">
      <h1>Test the Proxy API</h1>
      <div className="sub-tabs">
        <button className={tab === "selfie" ? "active" : ""} onClick={() => setTab("selfie")}>
          Selfie Verification
        </button>
        <button className={tab === "proxy" ? "active" : ""} onClick={() => setTab("proxy")}>
          Generic Proxy
        </button>
      </div>
      {tab === "selfie" ? <SelfieTestPanel /> : <ProxyTestPanel />}
    </div>
  );
}
