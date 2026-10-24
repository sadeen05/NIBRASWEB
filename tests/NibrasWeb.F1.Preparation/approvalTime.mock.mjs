import fs from 'node:fs';
import assert from 'node:assert/strict';
import ts from '../../frontend/node_modules/typescript/lib/typescript.js';
const source = fs.readFileSync(new URL('../../frontend/src/utils/approvalTime.ts', import.meta.url), 'utf8');
const js = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText;
const { formatApprovalUtc } = await import('data:text/javascript;base64,' + Buffer.from(js).toString('base64'));
const expected = formatApprovalUtc('2026-10-21T12:00:00Z', 'en');
assert.equal(formatApprovalUtc('2026-10-21T12:00:00', 'en'), expected);
assert.equal(formatApprovalUtc('2026-10-21T15:00:00+03:00', 'en'), expected);
for (const timezone of ['UTC', 'Asia/Amman', 'Pacific/Honolulu']) {
  process.env.TZ = timezone;
  assert.equal(formatApprovalUtc('2026-10-21T12:00:00', 'en'), expected);
}
assert.equal(formatApprovalUtc(null, 'en'), '—');
assert.equal(formatApprovalUtc('invalid', 'en'), '—');
assert.ok(formatApprovalUtc('2026-10-21T12:00:00Z', 'ar').endsWith(' UTC'));
console.log('APPROVAL_UTC_MOCK PASS=8 FAIL=0');
