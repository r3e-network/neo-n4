import test from 'node:test';
import assert from 'node:assert/strict';

import {
  reportTypes,
  validateReportEnvelope,
  assertRedactedReport,
} from '../../docs/experience-hub/data/reportSchemas.js';
import { sampleReports } from '../../docs/experience-hub/data/sampleReports.js';

test('all required phase-1 report types have redacted sample reports', () => {
  assert.deepEqual(Object.keys(sampleReports).sort(), reportTypes.toSorted());
  for (const type of reportTypes) {
    const result = validateReportEnvelope(sampleReports[type], type);
    assert.equal(result.ok, true, `${type}: ${result.errors.join(', ')}`);
    assert.doesNotThrow(() => assertRedactedReport(sampleReports[type]));
  }
});

test('report validation rejects wrong type and missing metadata', () => {
  const report = { ...sampleReports['devnet-report'], type: 'validation-report' };
  const result = validateReportEnvelope(report, 'devnet-report');
  assert.equal(result.ok, false);
  assert.match(result.errors.join('\n'), /type must be devnet-report/);

  const incomplete = { type: 'devnet-report' };
  const missing = validateReportEnvelope(incomplete, 'devnet-report');
  assert.equal(missing.ok, false);
  assert.match(missing.errors.join('\n'), /schemaVersion/);
  assert.match(missing.errors.join('\n'), /repoCommit/);
});

test('redaction guard rejects secret-like keys and values', () => {
  assert.throws(
    () => assertRedactedReport({
      ...sampleReports['deployment-receipt'],
      payload: { privateKey: 'Kx1234567890' },
    }),
    /secret-like key/i,
  );

  assert.throws(
    () => assertRedactedReport({
      ...sampleReports['deployment-receipt'],
      payload: { note: 'mnemonic phrase should never be present' },
    }),
    /secret-like value/i,
  );
});
