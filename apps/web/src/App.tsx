import { Link, NavLink, Outlet, useNavigate } from 'react-router-dom';
import { useAuth } from './auth/AuthContext';

/** Application shell: the header, the navigation, and wherever the router puts the page. */
export function App() {
  const auth = useAuth();
  const navigate = useNavigate();
  return (
    <div className="app">
      <header className="app__header">
        <Link className="app__brand" to="/tickets">
          Northwind Support
        </Link>

        <nav className="app__nav" aria-label="Main">
          <NavLink to="/tickets">Tickets</NavLink>
          <NavLink to="/customers">Customers</NavLink>
          <span>Signed in as {auth.user?.fullName}</span>
          <button type="button" className="button button--ghost" onClick={() => {
            auth.logout(); void navigate('/login', { replace: true });
          }}>Sign out</button>
        </nav>
      </header>

      <main className="app__main">
        <Outlet />
      </main>
    </div>
  );
}
