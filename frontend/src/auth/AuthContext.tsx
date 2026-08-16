import { createContext, useContext, useMemo, useState, type ReactNode } from "react";
import type { Role } from "../types/api";

interface StoredSession {
  username: string;
  role: Role;
  token: string;
  apiKey: string;
  expiresAtUtc: string;
}

interface AuthState {
  session: StoredSession | null;
  login: (session: StoredSession) => void;
  logout: () => void;
}

const STORAGE_KEY = "xds-ghc-verification.session";

const AuthContext = createContext<AuthState | null>(null);

function readStoredSession(): StoredSession | null {
  const raw = sessionStorage.getItem(STORAGE_KEY);
  if (!raw) return null;
  try {
    const parsed = JSON.parse(raw) as StoredSession;
    if (new Date(parsed.expiresAtUtc) <= new Date()) {
      sessionStorage.removeItem(STORAGE_KEY);
      return null;
    }
    return parsed;
  } catch {
    return null;
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<StoredSession | null>(readStoredSession);

  const value = useMemo<AuthState>(
    () => ({
      session,
      login: (newSession) => {
        sessionStorage.setItem(STORAGE_KEY, JSON.stringify(newSession));
        setSession(newSession);
      },
      logout: () => {
        sessionStorage.removeItem(STORAGE_KEY);
        setSession(null);
      },
    }),
    [session],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) {
    throw new Error("useAuth must be used within an AuthProvider");
  }
  return ctx;
}

export type { StoredSession };
