import { createServer } from 'node:net';

/** Prefer the assessment port, but leave an existing service running when it is occupied. */
export async function availablePort(preferred = 0) {
  if (!Number.isInteger(preferred) || preferred < 0 || preferred > 65535) {
    throw new RangeError('Port must be an integer between 0 and 65535.');
  }
  const server = createServer();
  try {
    await new Promise((resolve, reject) => {
      server.once('error', reject);
      server.listen({ port: preferred, host: '127.0.0.1', exclusive: true }, resolve);
    });
    const selected = server.address().port;
    await new Promise((resolve, reject) => server.close((error) => error ? reject(error) : resolve()));
    return selected;
  } catch (error) {
    if (preferred !== 0 && (error.code === 'EADDRINUSE' || error.code === 'EACCES')) return availablePort();
    throw error;
  }
}
