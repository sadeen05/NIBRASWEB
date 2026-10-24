import fs from 'node:fs';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { fileURLToPath, pathToFileURL } from 'node:url';
import ts from '../../frontend/node_modules/typescript/lib/typescript.js';

const source = fs.readFileSync(new URL('../../frontend/devApiTarget.ts', import.meta.url), 'utf8');
const js = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
}).outputText;
const { resolveDevApiTarget } = await import('data:text/javascript;base64,' + Buffer.from(js).toString('base64'));
let passed = 0;

for (const [command, mode] of [['serve', 'development'], ['serve', 'custom'], ['build', 'production']]) {
  assert.equal(resolveDevApiTarget(command, mode, undefined), 'http://localhost:5181');
  passed++;
}
for (const target of ['http://localhost:5199', 'http://127.0.0.1:5199', 'http://[::1]:5199']) {
  assert.equal(resolveDevApiTarget('serve', 'f1-acceptance', target), target);
  passed++;
}
for (const target of [
  undefined, '', 'http://127.0.0.1:5181', 'http://127.0.0.1:5200',
  'https://127.0.0.1:5199', 'http://0.0.0.0:5199', 'http://192.168.1.2:5199',
  'http://example.invalid:5199', 'http://localhost.example.invalid:5199',
  'http://user:dummy@localhost:5199', 'http://localhost:5199/api',
  'http://localhost:5199?target=external', 'http://localhost:5199#fragment',
  'http://127.1:5199', 'http://2130706433:5199', 'http://%6cocalhost:5199',
  ' http://localhost:5199', 'http://localhost:5199/',
]) {
  assert.throws(() => resolveDevApiTarget('serve', 'f1-acceptance', target), /Test API proxy requires/);
  passed++;
}
for (const [command, mode, preview] of [
  ['serve', 'development', false], ['serve', 'production', false],
  ['build', 'f1-acceptance', false], ['serve', 'f1-acceptance', true],
]) {
  assert.throws(() => resolveDevApiTarget(command, mode, 'http://127.0.0.1:5199', preview));
  passed++;
}
assert.throws(() => resolveDevApiTarget('build', 'f1-acceptance', undefined));
passed++;

for (const apiBase of ['', '  ', '/api', ' /api ']) {
  assert.equal(resolveDevApiTarget('serve', 'f1-acceptance', 'http://127.0.0.1:5199', false, apiBase), 'http://127.0.0.1:5199');
  passed++;
}
for (const apiBase of ['http://localhost:5181/api', 'https://example.invalid/api', '//example.invalid/api', '/other-api']) {
  assert.throws(() => resolveDevApiTarget('serve', 'f1-acceptance', 'http://127.0.0.1:5199', false, apiBase));
  passed++;
}

// Load the real Vite config without starting a server, browser, or API.
const require = createRequire(new URL('../../frontend/package.json', import.meta.url));
const { loadConfigFromFile } = await import(pathToFileURL(require.resolve('vite')).href);
const configFile = fileURLToPath(new URL('../../frontend/vite.config.ts', import.meta.url));
const original = process.env.NIBRAS_TEST_API_TARGET;
const originalBase = process.env.VITE_API_BASE_URL;
try {
  process.env.VITE_API_BASE_URL = '/api';
  for (const [command, mode, target] of [
    ['serve', 'development', undefined],
    ['build', 'production', undefined],
    ['serve', 'f1-acceptance', 'http://127.0.0.1:5199'],
  ]) {
    if (target === undefined) delete process.env.NIBRAS_TEST_API_TARGET;
    else process.env.NIBRAS_TEST_API_TARGET = target;
    const loaded = await loadConfigFromFile({ command, mode, isPreview: false }, configFile);
    assert.ok(loaded);
    assert.equal(loaded.config.server.proxy['/api'].target, target ?? 'http://localhost:5181');
    if (mode === 'f1-acceptance') assert.equal(loaded.config.server.host, '127.0.0.1');
    passed++;
  }
  // Verify fail-closed wiring in the real config, including client-base bypass.
  for (const [command, mode, target, apiBase] of [
    ['serve', 'development', 'http://127.0.0.1:5199', '/api'],
    ['serve', 'f1-acceptance', undefined, '/api'],
    ['serve', 'f1-acceptance', 'http://127.0.0.1:5199', 'https://example.invalid/api'],
    ['build', 'f1-acceptance', 'http://127.0.0.1:5199', '/api'],
  ]) {
    if (target === undefined) delete process.env.NIBRAS_TEST_API_TARGET;
    else process.env.NIBRAS_TEST_API_TARGET = target;
    process.env.VITE_API_BASE_URL = apiBase;
    await assert.rejects(
      () => loadConfigFromFile({ command, mode, isPreview: false }, configFile, undefined, 'silent'),
      /Test API proxy requires/,
    );
    passed++;
  }
} finally {
  if (originalBase === undefined) delete process.env.VITE_API_BASE_URL;
  else process.env.VITE_API_BASE_URL = originalBase;
  if (original === undefined) delete process.env.NIBRAS_TEST_API_TARGET;
  else process.env.NIBRAS_TEST_API_TARGET = original;
}
console.log('DEV_API_PROXY_MOCK PASS=' + passed + ' FAIL=0 (no server started)');