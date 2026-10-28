import { Navigate, Outlet, useLocation } from 'react-router-dom';
import { ErrorMessage } from '../components/ErrorMessage';
import { Spinner } from '../components/Spinner';
import { useAuth } from './AuthContext';

export function ProtectedRoute() {
  const auth = useAuth();
  const location = useLocation();
  if (auth.status === 'checking') return <Spinner label="Checking your session" />;
  if (auth.status === 'error') return <section>
    <ErrorMessage message={auth.error ?? 'Unable to validate your session.'} onRetry={auth.retry} />
    <button type="button" className="button button--ghost" onClick={auth.logout}>Return to sign in</button>
  </section>;
  if (auth.status === 'anonymous') return <Navigate to="/login" replace
    state={{ from: location.pathname + location.search + location.hash }} />;
  return <Outlet />;
}
