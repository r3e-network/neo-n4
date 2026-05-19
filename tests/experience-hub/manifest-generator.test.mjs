import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, readFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

import { buildManifest, writeManifest } from '../../tools/experience-hub/generate-manifest.mjs';

test('manifest generator discovers NeoHub contracts, tools, docs, and workflows', async () => {
  const root = await mkdtemp(join(tmpdir(), 'neo-n4-manifest-'));
  await mkdir(join(root, 'contracts', 'NeoHub.SharedBridge'), { recursive: true });
  await mkdir(join(root, 'tools', 'Neo.Stack.Cli'), { recursive: true });
  await mkdir(join(root, 'docs'), { recursive: true });
  await writeFile(join(root, 'docs', 'interactive-runtime.md'), '# Interactive Runtime');

  const manifest = await buildManifest(root);
  assert.deepEqual(manifest.contracts.map((item) => item.name), ['NeoHub.SharedBridge']);
  assert.deepEqual(manifest.tools.map((item) => item.name), ['Neo.Stack.Cli']);
  assert.equal(manifest.docs.some((item) => item.path === 'docs/interactive-runtime.md'), true);
  assert.equal(manifest.workflows.some((item) => item.id === 'deposit'), true);
  assert.equal(manifest.da.defaultProvider, 'NeoFS');
  assert.equal(manifest.vm.defaultProfile, 'NeoVM2/RISC-V');
});

test('writeManifest creates deterministic JSON', async () => {
  const root = await mkdtemp(join(tmpdir(), 'neo-n4-manifest-write-'));
  await mkdir(join(root, 'contracts', 'NeoHub.TokenRegistry'), { recursive: true });
  await mkdir(join(root, 'tools', 'Neo.Hub.Deploy'), { recursive: true });
  await mkdir(join(root, 'docs'), { recursive: true });

  const out = join(root, 'docs', 'experience-hub', 'data', 'neo-n4.manifest.json');
  await writeManifest(root, out);
  const json = JSON.parse(await readFile(out, 'utf8'));

  assert.equal(json.schemaVersion, '1.0.0');
  assert.equal(json.contracts[0].name, 'NeoHub.TokenRegistry');
  assert.equal(json.tools[0].name, 'Neo.Hub.Deploy');
});
