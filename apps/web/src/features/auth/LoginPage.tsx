import { useState } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../../auth/AuthContext';
import { returnPath } from '../../auth/returnPath';
import { Field } from '../../components/Field';
import { Spinner } from '../../components/Spinner';
import { toErrorMessage } from '../../api/client';

export function LoginPage() {
  const auth = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const state: unknown = location.state;
  const destination = returnPath(typeof state === 'object' && state !== null && 'from' in state ? state.from : undefined);
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [error, setError] = useState<string>();
  const [pending, setPending] = useState(false);
  if (auth.status === 'authenticated') return <Navigate to={destination} replace />;
  if (auth.status === 'checking') return <Spinner label="Checking your session" />;

  const submit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (pending) return;
    const invalid: Record<string, string> = {};
    if (email.trim().length > 256 || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim())) invalid.email = 'Enter a valid email address.';
    if (password.length < 1 || password.length > 1024) invalid.password = 'Enter a password of 1-1024 characters.';
    setErrors(invalid);
    setError(undefined);
    if (Object.keys(invalid).length) return;
    setPending(true);
    try {
      await auth.signIn({ email: email.trim(), password });
      setPassword('');
      await navigate(destination, { replace: true });
    } catch (caught) { setError(toErrorMessage(caught, 'Could not sign in.')); }
    finally { setPending(false); }
  };

  return <main className="app__main"><section className="login-panel">
    <h1>Sign in to Northwind Support</h1>
    <form className="card form" aria-label="Sign in" noValidate onSubmit={(event) => void submit(event)}>
      <Field id="login-email" label="Email" error={errors.email}>
        {(props) => <input {...props} type="email" autoComplete="username" value={email}
          onChange={(event) => setEmail(event.target.value)} disabled={pending} />}
      </Field>
      <Field id="login-password" label="Password" error={errors.password}>
        {(props) => <input {...props} type="password" autoComplete="current-password" value={password}
          onChange={(event) => setPassword(event.target.value)} disabled={pending} />}
      </Field>
      {error && <p role="alert" className="field__error">{error}</p>}
      {auth.status === 'error' && <p role="alert">{auth.error}</p>}
      <button type="submit" className="button button--primary" disabled={pending}>{pending ? 'Signing in...' : 'Sign in'}</button>
    </form>
  </section></main>;
}
