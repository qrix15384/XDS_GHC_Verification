import { useEffect, useState, type FormEvent } from "react";
import { api, ApiError } from "../api/client";
import { useAuth } from "../auth/AuthContext";
import type { ProxyUser } from "../types/api";

export function UsersPage() {
  const { session } = useAuth();
  const auth = session!;
  const [users, setUsers] = useState<ProxyUser[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [newUsername, setNewUsername] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [newRole, setNewRole] = useState<"Admin" | "Standard">("Standard");

  async function reload() {
    try {
      setUsers(await api.listUsers(auth));
    } catch (err) {
      setError(err instanceof ApiError ? err.detail : "Failed to load users.");
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
      await api.createUser(auth, newUsername, newPassword, newRole);
      setNewUsername("");
      setNewPassword("");
      setNewRole("Standard");
      await reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.detail : "Failed to create user.");
    }
  }

  async function handleToggleActive(user: ProxyUser) {
    setError(null);
    try {
      await api.updateUser(auth, user.id, user.role, !user.isActive);
      await reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.detail : "Failed to update user.");
    }
  }

  async function handleToggleRole(user: ProxyUser) {
    setError(null);
    const newUserRole = user.role === "Admin" ? "Standard" : "Admin";
    try {
      await api.updateUser(auth, user.id, newUserRole, user.isActive);
      await reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.detail : "Failed to update user.");
    }
  }

  async function handleResetPassword(user: ProxyUser) {
    const newPasswordValue = window.prompt(`New password for ${user.username}:`);
    if (!newPasswordValue) return;
    setError(null);
    try {
      await api.resetPassword(auth, user.id, newPasswordValue);
    } catch (err) {
      setError(err instanceof ApiError ? err.detail : "Failed to reset password.");
    }
  }

  async function handleDelete(user: ProxyUser) {
    if (!window.confirm(`Delete user "${user.username}"?`)) return;
    setError(null);
    try {
      await api.deleteUser(auth, user.id);
      await reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.detail : "Failed to delete user.");
    }
  }

  return (
    <div className="page">
      <h1>Manage Proxy Users</h1>

      <form className="inline-form" onSubmit={handleCreate}>
        <input
          placeholder="Username"
          value={newUsername}
          onChange={(e) => setNewUsername(e.target.value)}
          required
        />
        <input
          placeholder="Password"
          type="password"
          value={newPassword}
          onChange={(e) => setNewPassword(e.target.value)}
          required
          minLength={8}
        />
        <select value={newRole} onChange={(e) => setNewRole(e.target.value as "Admin" | "Standard")}>
          <option value="Standard">Standard</option>
          <option value="Admin">Admin</option>
        </select>
        <button type="submit">Create user</button>
      </form>

      {error && <p className="error">{error}</p>}

      <table>
        <thead>
          <tr>
            <th>Username</th>
            <th>Role</th>
            <th>Active</th>
            <th>Created</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {users.map((user) => (
            <tr key={user.id}>
              <td>{user.username}</td>
              <td>{user.role}</td>
              <td>{user.isActive ? "Yes" : "No"}</td>
              <td>{new Date(user.createdAtUtc).toLocaleString()}</td>
              <td className="actions">
                <button onClick={() => handleToggleRole(user)}>
                  Make {user.role === "Admin" ? "Standard" : "Admin"}
                </button>
                <button onClick={() => handleToggleActive(user)}>
                  {user.isActive ? "Deactivate" : "Activate"}
                </button>
                <button onClick={() => handleResetPassword(user)}>Reset password</button>
                <button className="danger" onClick={() => handleDelete(user)}>
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
