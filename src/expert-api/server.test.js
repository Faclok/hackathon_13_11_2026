import test from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import { once } from 'node:events';
import { spawn } from 'node:child_process';
import { setTimeout as delay } from 'node:timers/promises';

test('expert proxy forwards only POST vocabulary with the original authorization and payload', { timeout: 15000 }, async t => {
  const requests = [];
  const upstream = http.createServer(async (req, res) => {
    const chunks = [];
    for await (const chunk of req) chunks.push(chunk);
    if (req.url === '/health/ready') { res.end('{}'); return; }
    requests.push({ method: req.method, path: req.url, authorization: req.headers.authorization, type: req.headers['content-type'], body: JSON.parse(Buffer.concat(chunks).toString()) });
    const authorized = req.headers.authorization === 'Bearer test-producer';
    res.writeHead(authorized ? 200 : 403, { 'content-type': 'application/json' });
    res.end(JSON.stringify(authorized ? { ids: ['saved-word-id'] } : { title: 'Forbidden' }));
  });
  upstream.listen(0, '127.0.0.1'); await once(upstream, 'listening');
  t.after(() => { upstream.closeAllConnections(); upstream.close(); });
  const reserved = http.createServer(); reserved.listen(0, '127.0.0.1'); await once(reserved, 'listening');
  const port = reserved.address().port; await new Promise(resolve => reserved.close(resolve));
  const child = spawn(process.execPath, ['server.js'], {
    cwd: new URL('.', import.meta.url),
    env: { ...process.env, PORT: String(port), MAIN_API_URL: `http://127.0.0.1:${upstream.address().port}` },
    stdio: ['ignore', 'ignore', 'pipe']
  });
  t.after(async () => { if (child.exitCode === null) { const exit = once(child, 'exit'); child.kill(); await exit; } });
  let ready = false;
  for (let attempt = 0; attempt < 60 && !ready; attempt++) {
    try { ready = (await fetch(`http://127.0.0.1:${port}/health/ready`)).ok; } catch { await delay(30); }
  }
  assert.equal(ready, true, 'Proxy starts');
  const url = `http://127.0.0.1:${port}/api/vocabulary`;
  const payload = { languagePair: 'ba-ru', entries: [{ word: 'бер', translation: 'один' }] };
  const options = token => ({ method: 'POST', headers: { 'content-type': 'application/json', authorization: `Bearer ${token}` }, body: JSON.stringify(payload) });
  const response = await fetch(url, options('test-producer'));
  assert.equal(response.status, 200);
  assert.equal(response.headers.get('cache-control'), 'no-store');
  assert.deepEqual(await response.json(), { ids: ['saved-word-id'] });
  assert.deepEqual(requests[0], { method: 'POST', path: '/api/vocabulary', authorization: 'Bearer test-producer', type: 'application/json', body: payload });
  const denied = await fetch(url, options('test-participant'));
  assert.equal(denied.status, 403);
  assert.deepEqual(await denied.json(), { title: 'Forbidden' });
  for (const [path, method] of [['/api/vocabulary', 'GET'], ['/api/vocabulary', 'DELETE'], ['/api/vocabulary/other', 'POST']]) {
    const denied = await fetch(`http://127.0.0.1:${port}${path}`, { method });
    assert.equal(denied.status, 404);
  }
  assert.equal(requests.length, 2, 'Disallowed requests never reach core');
});
