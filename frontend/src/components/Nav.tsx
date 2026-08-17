import { NavLink, useNavigate } from "react-router-dom";
import { useAuth } from "../auth/AuthContext";

export function Nav() {
  const { session, logout } = useAuth();
  const navigate = useNavigate();

  if (!session) return null;

  return (
    <nav className="nav">
      <div className="nav-links">
        {session.role === "Admin" && (
          <>
            <NavLink to="/users" className={({ isActive }) => (isActive ? "active" : "")}>
              Users
            </NavLink>
            <NavLink to="/subscribers" className={({ isActive }) => (isActive ? "active" : "")}>
              Subscribers
            </NavLink>
          </>
        )}
        <NavLink to="/transactions" className={({ isActive }) => (isActive ? "active" : "")}>
          Transactions
        </NavLink>
        <NavLink to="/test-api" className={({ isActive }) => (isActive ? "active" : "")}>
          Test API
        </NavLink>
      </div>
      <div className="nav-user">
        <span>
          {session.username} ({session.role})
        </span>
        <button
          onClick={() => {
            logout();
            navigate("/login");
          }}
        >
          Log out
        </button>
      </div>
    </nav>
  );
}
