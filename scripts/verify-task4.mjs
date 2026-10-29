/** Task 4 end-to-end verification. Requires Node 22 and access to the Docker engine. */
import assert from 'node:assert/strict';
import { randomBytes } from 'node:crypto';
import { spawn } from 'node:child_process';
import { promises as fs } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { availablePort } from './verification-ports.mjs';

const root = fileURLToPath(new URL('../', import.meta.url));
const artifacts = path.join(root, 'artifacts');
await fs.mkdir(artifacts, { recursive: true });
const evidence = await fs.mkdtemp(path.join(artifacts, 'task4-'));
const project = `northwind-task4-${Date.now().toString(36)}-${randomBytes(3).toString('hex')}`;
assert.match(project, /^northwind-task4-[a-z0-9-]+$/);
const key = randomBytes(48).toString('base64');
const password = 'Local_Dev_Password1';
const pause = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

const apiPort = await availablePort(5080);
let sqlPort = await availablePort();
while (sqlPort === apiPort) sqlPort = await availablePort();
const proxyTarget = `http://localhost:${apiPort}`;
console.log(apiPort === 5080 ? 'Using API test port 5080.' :
  `Port 5080 is occupied; using ${apiPort} for the disposable API. Your existing service stays running.`);
const settings = { Jwt__SigningKey: key, MSSQL_SA_PASSWORD: password, ASPNETCORE_ENVIRONMENT: 'Development',
  SQLSERVER_PORT: String(sqlPort), API_PORT: String(apiPort), Jwt__Issuer: 'Northwind.SupportDesk',
  Jwt__Audience: 'Northwind.SupportDesk.Web', Jwt__TokenLifetimeMinutes: '60',
  Sla__BaseWindowHours__Critical: '4', Sla__BaseWindowHours__High: '8', Sla__BaseWindowHours__Medium: '24',
  Sla__BaseWindowHours__Low: '72', Sla__PremiumCustomerMultiplier: '0.5', Sla__MinimumWindowHours: '1',
  Sla__AtRiskThresholdPercent: '25', COMPOSE_BAKE: 'false' };
const env = { ...process.env, ...settings };
const envFile = path.join(evidence, 'verification.env');
await fs.writeFile(envFile, Object.entries(settings).map(([name, value]) => `${name}=${value}`).join('\n') + '\n');
const compose = ['compose', '--project-name', project, '--env-file', envFile, '-f', path.join(root, 'docker-compose.yml')];
let sequence = 0;
function run(command, args, { log = true, childEnv = env } = {}) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, { cwd: root, env: childEnv, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
    let output = '';
    child.stdout.on('data', (data) => { output += data; });
    child.stderr.on('data', (data) => { output += data; });
    child.once('error', reject);
    child.once('close', async (code) => {
      const safe = output.split(key).join('[redacted]');
      try {
        if (log) await fs.writeFile(path.join(evidence, `${String(++sequence).padStart(2, '0')}-${path.basename(command).replace(/[^a-z0-9]/gi, '')}.log`), safe);
        if (code !== 0) reject(new Error(`${command} exited ${code}:\n${safe.slice(-5000)}`));
        else resolve(output);
      } catch (error) { reject(error); }
    });
  });
}
const docker = (args, options) => run('docker', args, options);
const dc = (args, options) => docker([...compose, ...args], options);
const base = `http://127.0.0.1:${apiPort}`;
async function request(url, { token, body, method = body ? 'POST' : 'GET' } = {}) {
  return fetch(url, { method, headers: { ...(body ? { 'Content-Type': 'application/json' } : {}),
    ...(token ? { Authorization: `Bearer ${token}` } : {}) }, body: body ? JSON.stringify(body) : undefined,
    signal: AbortSignal.timeout(12000) });
}
async function healthy() {
  const deadline = Date.now() + 180000;
  while (Date.now() < deadline) {
    try { const response = await request(`${base}/health`); if (response.status === 200 && await response.text() === 'Healthy') return; }
    catch { /* SQL startup and EF migration are expected to take a moment. */ }
    await pause(1000);
  }
  throw new Error('The API did not become database-healthy within three minutes.');
}
async function login(origin) {
  const response = await request(`${origin}/api/auth/login`, { body: {
    email: 'alex.turner@northwind-support.example', password: 'Local_Only_Demo1!' } });
  assert.equal(response.status, 200, 'Seeded development login');
  return response.json();
}
async function sql(query) {
  return (await dc(['exec', '-T', 'sqlserver', '/opt/mssql-tools18/bin/sqlcmd', '-S', 'localhost', '-U', 'sa',
    '-P', password, '-C', '-d', 'SupportDesk', '-h', '-1', '-W', '-Q', `SET NOCOUNT ON; ${query}`], { log: false })).trim();
}
const hashQuery = "SELECT CONVERT(varchar(64), HASHBYTES('SHA2_256', STRING_AGG(CONVERT(nvarchar(max),PasswordHash),N'|') WITHIN GROUP (ORDER BY Id)),2) FROM Agents;";
let stackCreated = false;
let web;
let webLog = '';
async function stopWeb() {
  if (!web || web.exitCode !== null || web.signalCode !== null) return;
  if (process.platform === 'win32') await run('taskkill.exe', ['/PID', String(web.pid), '/T', '/F']);
  else process.kill(-web.pid, 'SIGTERM');
  web = undefined;
}
const result = { project, verifiedAtUtc: new Date().toISOString(), evidenceDirectory: path.relative(root, evidence),
  apiPort, sqlPort, proxyTarget, usedFallbackApiPort: apiPort !== 5080 };
try {
  console.log(`Verifying an isolated stack: ${project}`);
  // Inspect configuration internally, never log its secret-bearing environment section.
  const config = JSON.parse(await dc(['config', '--format', 'json'], { log: false }));
  for (const volume of Object.values(config.volumes)) {
    assert.equal(Boolean(volume.external), false);
    assert.ok(volume.name.startsWith(`${project}_`), 'Only this test project may own a volume');
  }
  assert.equal(config.services.sqlserver.container_name, undefined, 'A fixed name would collide with existing development SQL');
  assert.equal((await docker(['ps', '-a', '--filter', `label=com.docker.compose.project=${project}`, '-q'])).trim(), '');
  // This destructive reset is scoped to the unique, verified disposable project, never the developer stack.
  await dc(['down', '--volumes', '--remove-orphans']);
  stackCreated = true;
  console.log('Starting SQL and the API from an empty project volume...');
  await dc(['up', '--build', '-d']);
  await healthy();
  result.anonymousHealthStatus = 200;
  assert.equal((await request(`${base}/api/tickets`)).status, 401);
  const session = await login(base);
  const initial = await (await request(`${base}/api/tickets`, { token: session.token })).json();
  assert.equal(initial.totalCount, 40);
  result.initialSeededTickets = initial.totalCount;
  result.appliedMigrations = Number(await sql('SELECT COUNT(*) FROM __EFMigrationsHistory;'));
  result.agentsWithPasswordHashes = Number(await sql('SELECT COUNT(*) FROM Agents WHERE PasswordHash IS NOT NULL;'));
  assert.equal(result.agentsWithPasswordHashes, 6);
  const hashesBefore = await sql(hashQuery);
  console.log('Fresh migration, seed, anonymous health and authenticated list verified.');

  const user = (await dc(['exec', '-T', 'api', 'whoami'])).trim();
  const uid = (await dc(['exec', '-T', 'api', 'id', '-u'])).trim();
  assert.notEqual(user, 'root'); assert.notEqual(uid, '0');
  result.runtimeUser = user; result.runtimeUid = Number(uid);
  assert.equal((await dc(['exec', '-T', 'api', 'dotnet', '--list-sdks'])).trim(), '');
  const files = (await dc(['exec', '-T', 'api', 'find', '/app', '-type', 'f'])).split(/\r?\n/);
  const unexpected = files.filter((name) => /(^|\/)(appsettings[^/]*\.json|\.env(?:\..*)?)$|\.(?:cs|pfx|p12|pem|key)$/.test(name));
  assert.deepEqual(unexpected, [], 'Runtime image must not contain deployment configuration, sources or certificates');
  const imageEnv = JSON.parse(await docker(['image', 'inspect', 'northwind-support-api:local', '--format', '{{json .Config.Env}}'], { log: false }));
  assert.ok(imageEnv.every((entry) => !/^(Jwt__SigningKey|ConnectionStrings__Default|ASPNETCORE_ENVIRONMENT)=/i.test(entry)));
  const history = await docker(['history', '--no-trunc', '--format', '{{.CreatedBy}}', 'northwind-support-api:local']);
  assert.ok(!history.includes(key));
  result.runtimeContainsNoAppsettingsSourcesCertificatesOrSdk = true;

  console.log(`Starting the local web command against ${proxyTarget}...`);
  const webEnv = { ...env, VITE_API_PROXY_TARGET: proxyTarget, NO_COLOR: '1' };
  web = process.platform === 'win32'
    ? spawn('cmd.exe', ['/d', '/s', '/c', 'npm run web'], { cwd: root, env: webEnv, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] })
    : spawn('npm', ['run', 'web'], { cwd: root, env: webEnv, detached: true, stdio: ['ignore', 'pipe', 'pipe'] });
  web.stdout.on('data', (data) => { webLog += data; });
  web.stderr.on('data', (data) => { webLog += data; });
  let origin;
  const deadline = Date.now() + 90000;
  while (Date.now() < deadline) {
    const text = webLog.replace(/\u001b\[[0-9;]*m/g, '');
    const match = /Local:\s+(http:\/\/(?:localhost|127\.0\.0\.1):\d+)\//.exec(text);
    if (match) { origin = match[1]; break; }
    if (web.exitCode !== null) throw new Error(`Web command exited: ${text.slice(-3000)}`);
    await pause(300);
  }
  assert.ok(origin, 'Vite did not publish its loopback URL');
  assert.equal((await request(origin)).status, 200);
  const proxySession = await login(origin);
  const proxied = await (await request(`${origin}/api/tickets`, { token: proxySession.token })).json();
  assert.equal(proxied.totalCount, 40);
  const reportResponse = await request(`${origin}/api/reports/sla-summary?from=2000-01-01T00:00:00Z&to=2100-01-01T00:00:00Z&groupBy=agent`,
    { token: proxySession.token });
  assert.equal(reportResponse.status, 200);
  const reportRows = await reportResponse.json();
  assert.equal(reportRows.reduce((total, row) => total + row.total, 0), 40);
  result.viteProxySlaSummary = true;
  const createdResponse = await request(`${origin}/api/tickets`, { token: proxySession.token, body: {
    title: 'Task 4 container verification', description: 'Created through the local Vite proxy to test the container.',
    customerId: 1, categoryId: 1, requestedPriority: 'Low' } });
  assert.equal(createdResponse.status, 201);
  const ticket = await createdResponse.json();
  const escalationResponse = await request(`${origin}/api/tickets/${ticket.id}/escalate`, { token: proxySession.token,
    body: { reason: 'Container and proxy verification' } });
  assert.equal(escalationResponse.status, 200);
  const escalated = await escalationResponse.json();
  assert.equal(escalated.escalation.escalatedBy, `agent:${proxySession.user.id}`);
  result.viteProxyLoginListCreateEscalate = true;
  result.viteOrigin = origin;
  await stopWeb();
  await fs.writeFile(path.join(evidence, 'web.log'), webLog);

  await dc(['restart', 'api']);
  await healthy();
  assert.equal((await sql(hashQuery)), hashesBefore, 'Restart must preserve existing password hashes');
  assert.equal(Number(await sql('SELECT COUNT(*) FROM Tickets;')), 41);
  const persisted = await (await request(`${base}/api/tickets/${ticket.id}/escalations`, { token: session.token })).json();
  assert.equal(persisted.length, 1);
  result.restartPreservesTicketsHistoryAndCredentials = true;

  console.log('Checking health failure and recovery with only the disposable SQL service stopped...');
  await dc(['stop', 'sqlserver']);
  assert.equal((await request(`${base}/health`)).status, 503);
  result.databaseUnavailableHealthStatus = 503;
  await dc(['start', 'sqlserver']);
  await healthy();
  result.databaseRecoveryHealthStatus = 200;

  console.log('Rebuilding after a temporary C# source change to prove restore caching...');
  const source = path.join(root, 'apps/api/src/SupportDesk.Presentation/Health/DatabaseHealthCheck.cs');
  const original = await fs.readFile(source);
  const changed = Buffer.concat([original, Buffer.from(`\n// Task 4 cache verification ${project}\n`)]);
  await fs.writeFile(source, changed);
  let cacheLog;
  try { cacheLog = await docker(['compose', '--progress', 'plain', ...compose.slice(1), 'build', 'api']); }
  finally {
    assert.ok((await fs.readFile(source)).equals(changed), 'Source changed concurrently; refusing to overwrite a user edit');
    await fs.writeFile(source, original);
  }
  await fs.writeFile(path.join(evidence, 'restore-cache.log'), cacheLog);
  const restore = /^#(\d+) .*RUN dotnet restore [^\n]+/m.exec(cacheLog);
  assert.ok(restore, 'Restore build step was not found in plain-progress output');
  assert.match(cacheLog, new RegExp(`^#${restore[1]} CACHED\\r?$`, 'm'));
  result.restoreLayerCachedAfterCsChange = true;
  // Put the final image back in exact agreement with the restored source tree.
  await docker(['compose', '--progress', 'plain', ...compose.slice(1), 'build', 'api']);
  await docker(['pull', 'mcr.microsoft.com/dotnet/sdk:10.0']);
  result.runtimeImageBytes = Number((await docker(['image', 'inspect', 'northwind-support-api:local', '--format', '{{.Size}}'])).trim());
  result.sdkImageBytes = Number((await docker(['image', 'inspect', 'mcr.microsoft.com/dotnet/sdk:10.0', '--format', '{{.Size}}'])).trim());
  assert.ok(result.runtimeImageBytes < result.sdkImageBytes);
  result.runtimeImageTag = 'northwind-support-api:local';
  result.sdkImageTag = 'mcr.microsoft.com/dotnet/sdk:10.0';
  result.complete = true;
  console.log(`Verified runtime image: ${result.runtimeImageBytes} bytes; SDK: ${result.sdkImageBytes} bytes; user: ${user}.`);
} catch (error) {
  result.failure = String(error).split(key).join('[redacted]');
  if (stackCreated) {
    try { await dc(['logs', '--no-color', '--tail', '100', 'api', 'sqlserver']); }
    catch { /* Preserve the original verification error if diagnostic collection also fails. */ }
  }
  throw error;
} finally {
  const cleanupErrors = [];
  try {
    try { await stopWeb(); } catch (error) { cleanupErrors.push(error); }
    try {
      if (stackCreated) await dc(['down', '--volumes', '--remove-orphans']);
      result.disposableStackRemoved = true;
    } catch (error) { cleanupErrors.push(error); result.disposableStackRemoved = false; }
    result.cleanupErrors = cleanupErrors.map((error) => String(error).split(key).join('[redacted]'));
    await fs.writeFile(path.join(evidence, 'web.log'), webLog);
    await fs.writeFile(path.join(evidence, 'result.json'), JSON.stringify(result, null, 2) + '\n');
  } finally {
    // Remove our exact temporary key file even if Docker or evidence collection fails.
    await fs.rm(envFile, { force: true });
  }
  console.log(`Verification evidence: ${path.relative(root, evidence)}; disposable stack removed: ${result.disposableStackRemoved}.`);
  if (cleanupErrors.length) throw new AggregateError(cleanupErrors, 'Verification cleanup needs attention; see result.json.');
}
