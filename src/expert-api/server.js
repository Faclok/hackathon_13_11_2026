import http from 'node:http';

const upstream = process.env.MAIN_API_URL || 'http://main-api:8080';
const port = Number(process.env.PORT || 8080);
const uuid = '[0-9a-fA-F-]{36}';
const routes = [
  ['POST', /^\/auth\/(sign-in|refresh)$/],
  ['GET', /^\/api\/me$/],
  ['POST', /^\/api\/expert\/imports$/],
  // Temporary vocabulary uploader in the expert client; core checks content.deliver.
  ['POST', /^\/api\/vocabulary$/],
  ['GET', /^\/api\/expert\/untranslated$/],
  ['POST', new RegExp(`^/api/expert/fragments/${uuid}/translation$`)],
  ['GET', /^\/api\/expert\/(dashboard|work|cases)$/],
  ['GET', /^\/api\/expert\/preferences$/],
  ['PUT', /^\/api\/expert\/preferences$/],
  ['GET', new RegExp(`^/api/expert/cases/${uuid}(?:/history)?$`)],
  ['POST', new RegExp(`^/api/expert/cases/${uuid}/(?:claim|variants|resolution)$`)],
  ['DELETE', new RegExp(`^/api/expert/cases/${uuid}/claim$`)],
  ['GET', /^\/api\/policies\/distribution$/],
  ['POST', /^\/api\/policies\/distribution$/],
  ['GET', /^\/health\/ready$/]
];

http.createServer(async (request, response) => {
  const url = new URL(request.url, 'http://localhost');
  if (!routes.some(([method, pattern]) => method === request.method && pattern.test(url.pathname))) {
    response.writeHead(404).end();
    return;
  }
  try {
    const chunks = [];
    let size = 0;
    for await (const chunk of request) {
      size += chunk.length;
      if (size > 1024 * 1024) {
        response.writeHead(413).end();
        return;
      }
      chunks.push(chunk);
    }
    const headers = {};
    if (request.headers.authorization) headers.authorization = request.headers.authorization;
    if (request.headers['content-type']) headers['content-type'] = request.headers['content-type'];
    if (request.headers['idempotency-key']) headers['idempotency-key'] = request.headers['idempotency-key'];
    const result = await fetch(upstream + url.pathname + url.search, {
      method: request.method,
      headers,
      body: chunks.length ? Buffer.concat(chunks) : undefined,
      signal: AbortSignal.timeout(15000)
    });
    response.writeHead(result.status, {
      'content-type': result.headers.get('content-type') || 'application/json',
      'cache-control': 'no-store'
    });
    response.end(Buffer.from(await result.arrayBuffer()));
  } catch {
    response.writeHead(502, { 'content-type': 'application/json' }).end(JSON.stringify({ title: 'Сервис экспертизы временно недоступен.' }));
  }
}).listen(port, '0.0.0.0');
