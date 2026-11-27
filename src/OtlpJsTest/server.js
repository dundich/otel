const http = require('http');
const { randomBytes } = require('crypto');

const port = Number(process.env.PORT || 8080);
const name = process.env.SERVICE_NAME || 'otlp-js-test';
const version = '1.0.0';
const registry = process.env.DASHBOARD_REGISTRY_URL || 'http://aspire-dashboard:18888/api/registry';
const otlp = process.env.OTEL_EXPORTER_OTLP_ENDPOINT || 'http://aspire-dashboard:18890';

let count = 0;

const resource = () => ({
  attributes: [{ key: 'service.name', value: { stringValue: name } }]
});

const panel = () => ({
  id: 'JsCounterPanel',
  title: 'JS счётчик',
  version,
  description: 'Минимальный JavaScript app',
  order: 12,
  kind: 'panel',
  refreshIntervalMs: 1000,
  elements: [{ type: 'label', text: 'все будет Js', order: 0 }],
  fields: [{
    name: 'Count', label: 'Значение', inputType: 'readonly',
    order: 1, placeholder: null, required: false, value: count
  }],
  actions: []
});

const json = (res, value, status = 200) => {
  res.writeHead(status, { 'content-type': 'application/json; charset=utf-8' });
  res.end(JSON.stringify(value));
};

http.createServer((req, res) => {
  const path = new URL(req.url, 'http://localhost').pathname;

  if (req.method === 'GET' && path === '/dashboard/panels') return json(res, [panel()]);
  if (req.method === 'GET' && path === '/dashboard/panels/JsCounterPanel') return json(res, panel());
  if (req.method === 'GET' && path === '/api/state') return json(res, { count });
  if (req.method === 'GET' && path === '/healthz') return json(res, { status: 'ok' });

  if (req.method === 'GET' && path === '/') {
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' });
    return res.end(`<!doctype html><meta charset="utf-8"><title>все будет Js</title>
      <h1>все будет Js</h1><p>count: <b id="count">${count}</b></p>
      <script>setInterval(async () => count.textContent = (await (await fetch('/api/state')).json()).count, 1000)</script>`);
  }

  json(res, { error: 'not found' }, 404);
}).listen(port, '0.0.0.0');

const register = async () => {
  try {
    await fetch(`${registry}/register`, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({
        id: name, name, version,
        internalUrl: process.env.SERVICE_INTERNAL_URL || 'http://otlp-js-test:8080',
        publicUrl: process.env.SERVICE_PUBLIC_URL || 'http://localhost:8083'
      })
    });
  } catch {
    // dashboard can start later; the next tick retries
  }
};

const exportMetric = async () => {
  try {
    await fetch(`${otlp}/v1/metrics`, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({
        resourceMetrics: [{
          resource: resource(),
          scopeMetrics: [{
            scope: { name: 'otlp-js-test', version },
            metrics: [{
              name: 'js.counter.value',
              description: 'Current value of the JavaScript demo counter',
              unit: '1',
              gauge: {
                dataPoints: [{
                  timeUnixNano: String(BigInt(Date.now()) * 1000000n),
                  asInt: String(count)
                }]
              }
            }]
          }]
        }]
      })
    });
  } catch {
    // best effort: the metric is sent again in two seconds
  }
};

const exportTrace = async () => {
  const now = BigInt(Date.now()) * 1000000n;

  try {
    await fetch(`${otlp}/v1/traces`, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({
        resourceSpans: [{
          resource: resource(),
          scopeSpans: [{
            scope: { name: 'otlp-js-test', version },
            spans: [{
              traceId: randomBytes(16).toString('hex'),
              spanId: randomBytes(8).toString('hex'),
              name: '🚀 js.counter.tick',
              kind: 1,
              startTimeUnixNano: String(now),
              endTimeUnixNano: String(now + 1000000n),
              attributes: [{
                key: 'counter.value',
                value: { intValue: String(count) }
              }],
              status: { code: 1 }
            }]
          }]
        }]
      })
    });
  } catch {
    // best effort: the next tick sends another span
  }
};

const exportLog = async () => {
  try {
    await fetch(`${otlp}/v1/logs`, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({
        resourceLogs: [{
          resource: resource(),
          scopeLogs: [{
            scope: { name: 'otlp-js-test', version },
            logRecords: [{
              timeUnixNano: String(BigInt(Date.now()) * 1000000n),
              severityNumber: 9,
              severityText: 'INFO',
              body: { stringValue: `🚀 JS counter tick value=${count}` },
              attributes: [{
                key: 'counter.value',
                value: { intValue: String(count) }
              }]
            }]
          }]
        }]
      })
    });
  } catch {
    // best effort: the next tick sends another log record
  }
};

register();
exportMetric();
exportTrace();
setInterval(register, 10000);
setInterval(exportMetric, 2000);
setInterval(() => {
  count++;
  exportTrace();

  // Info-лог с ракетой — раз в 10 секунд, чтобы не засорять Structured logs.
  if (count % 10 === 0) {
    exportLog();
  }
}, 1000);
