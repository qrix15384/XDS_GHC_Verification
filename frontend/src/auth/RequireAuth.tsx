import type { ReactNode } from "react";
import { Navigate } from "react-router-dom";
import { useAuth } from "./AuthContext";
import type { Role } from "../types/api";

export function RequireAuth({ children, role }: { children: ReactNode; role?: Role }) {
  const { session } = useAuth();

  if (!session) {
    return <Navigate to="/login" replace />;
  }
  if (role && session.role !== role) {
    return <Navigate to="/transactions" replace />;
  }

  return <>{children}</>;
}
