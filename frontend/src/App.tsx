import { Navigate, Route, Routes } from "react-router-dom";
import { AuthProvider } from "./auth/AuthContext";
import { RequireAuth } from "./auth/RequireAuth";
import { Nav } from "./components/Nav";
import { LoginPage } from "./pages/LoginPage";
import { UsersPage } from "./pages/UsersPage";
import { SubscribersPage } from "./pages/SubscribersPage";
import { TransactionsPage } from "./pages/TransactionsPage";
import { TestApiPage } from "./pages/TestApiPage";

export default function App() {
  return (
    <AuthProvider>
      <Nav />
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route
          path="/users"
          element={
            <RequireAuth role="Admin">
              <UsersPage />
            </RequireAuth>
          }
        />
        <Route
          path="/subscribers"
          element={
            <RequireAuth role="Admin">
              <SubscribersPage />
            </RequireAuth>
          }
        />
        <Route
          path="/transactions"
          element={
            <RequireAuth>
              <TransactionsPage />
            </RequireAuth>
          }
        />
        <Route
          path="/test-api"
          element={
            <RequireAuth>
              <TestApiPage />
            </RequireAuth>
          }
        />
        <Route path="*" element={<Navigate to="/transactions" replace />} />
      </Routes>
    </AuthProvider>
  );
}
