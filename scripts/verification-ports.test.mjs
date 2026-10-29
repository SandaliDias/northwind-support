import assert from 'node:assert/strict';
import { createServer, connect } from 'node:net';
import test from 'node:test';
import { availablePort } from './verification-ports.mjs';

test('an occupied preferred port falls back without stopping its owner', async () => {
  const existing = createServer((socket) => socket.end('existing service'));
  await new Promise((resolve) => existing.listen(0, '127.0.0.1', resolve));
  try {
    const occupied = existing.address().port;
    const selected = await availablePort(occupied);
    assert.notEqual(selected, occupied);
    const message = await new Promise((resolve, reject) => {
      const socket = connect(occupied, '127.0.0.1');
      let text = '';
      socket.on('data', (chunk) => { text += chunk; });
      socket.on('end', () => resolve(text));
      socket.on('error', reject);
    });
    assert.equal(message, 'existing service');
  } finally { await new Promise((resolve) => existing.close(resolve)); }
});

test('an available preferred port is retained', async () => {
  const preferred = await availablePort();
  assert.equal(await availablePort(preferred), preferred);
});

test('invalid port values fail clearly', async () => {
  for (const port of [-1, 65536, 1.5]) await assert.rejects(availablePort(port), RangeError);
});
