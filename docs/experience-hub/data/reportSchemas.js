export const reportTypes = Object.freeze([
  'chain-config-report',
  'deployment-plan',
  'deployment-receipt',
  'devnet-report',
  'neofs-da-report',
  'bridge-drill-report',
  'validation-report',
]);

const requiredEnvelopeFields = Object.freeze([
  'schemaVersion',
  'repoCommit',
  'generatedAt',
  'tool',
  'network',
  'redaction',
  'type',
  'summary',
  'payload',
]);

const secretKeyPattern = /(private|secret|mnemonic|seed|password|token|credential|signingKey)/i;
const secretValuePattern = /(mnemonic phrase|private key|BEGIN PRIVATE KEY|Kx[0-9A-Za-z]{8,}|L[0-9A-Za-z]{20,})/i;

export function validateReportEnvelope(report, expectedType) {
  const errors = [];
  if (!report || typeof report !== 'object' || Array.isArray(report)) {
    return { ok: false, errors: ['report must be an object'] };
  }

  for (const field of requiredEnvelopeFields) {
    if (!(field in report)) errors.push(`${field} is required`);
  }

  if (report.type !== expectedType) errors.push(`type must be ${expectedType}`);
  if (!reportTypes.includes(expectedType)) errors.push(`unknown report type ${expectedType}`);
  if (typeof report.schemaVersion !== 'string' || !report.schemaVersion.match(/^1\./)) {
    errors.push('schemaVersion must be a v1 string');
  }
  if (typeof report.repoCommit !== 'string' || report.repoCommit.length < 7) {
    errors.push('repoCommit must identify the source commit');
  }
  if (report.redaction?.secrets !== 'removed') {
    errors.push('redaction.secrets must be removed');
  }

  return { ok: errors.length === 0, errors };
}

export function assertRedactedReport(report) {
  walk(report, []);
}

function walk(value, path) {
  if (Array.isArray(value)) {
    value.forEach((item, index) => walk(item, [...path, String(index)]));
    return;
  }

  if (!value || typeof value !== 'object') {
    if (typeof value === 'string' && secretValuePattern.test(value)) {
      throw new Error(`secret-like value at ${path.join('.')}`);
    }
    return;
  }

  for (const [key, child] of Object.entries(value)) {
    if (!isAllowedRedactionMetadataKey(path, key) && secretKeyPattern.test(key)) {
      throw new Error(`secret-like key at ${[...path, key].join('.')}`);
    }
    walk(child, [...path, key]);
  }
}

function isAllowedRedactionMetadataKey(path, key) {
  return path.length === 1 && path[0] === 'redaction' && (key === 'secrets' || key === 'credentials');
}
