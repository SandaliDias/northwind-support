/** Only an internal route may become a post-login destination. */
export function returnPath(value: unknown): string {
  if (typeof value !== 'string' || !value.startsWith('/') || value.startsWith('//')
    || value.includes('\\') || Array.from(value).some((character) => character.charCodeAt(0) < 32)) return '/tickets';
  try {
    const url = new URL(value, window.location.origin);
    return url.origin === window.location.origin && url.pathname !== '/login' ? value : '/tickets';
  } catch { return '/tickets'; }
}
