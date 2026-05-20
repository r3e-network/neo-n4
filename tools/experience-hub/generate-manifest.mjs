import { mkdir, readdir, readFile, stat, writeFile } from 'node:fs/promises';
import { dirname, join, relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const workflows = Object.freeze([
  { id: 'deposit', label: 'L1 to L2 deposit' },
  { id: 'batch', label: 'Batch settlement' },
  { id: 'proof', label: 'ZK proof and contract verifier route' },
  { id: 'withdrawal', label: 'L2 to L1 withdrawal' },
  { id: 'external', label: 'External bridge' },
  { id: 'challenge', label: 'Challenge and recovery' },
]);

export async function buildManifest(root) {
  const [contracts, tools, docs] = await Promise.all([
    discoverDirectories(root, 'contracts', (name) => name.startsWith('NeoHub.')),
    discoverDirectories(root, 'tools', () => true),
    discoverDocs(root),
  ]);

  return {
    schemaVersion: '1.0.0',
    generatedAt: new Date().toISOString(),
    contracts,
    tools,
    docs,
    workflows,
    da: {
      defaultProvider: 'NeoFS',
      commitmentPath: 'NeoHub.DARegistry + NeoHub.DAValidator',
    },
    vm: {
      defaultProfile: 'NeoVM2/RISC-V',
      optionalProfiles: ['EVM', 'WASM', 'Move', 'Custom'],
    },
    boundaries: {
      neohub: 'deployable L1 contracts',
      browser: 'read-only',
      signing: 'wallets, CLIs, node processes, and contracts',
    },
  };
}

export async function writeManifest(root, outFile) {
  const manifest = await buildManifest(root);
  await mkdir(dirname(outFile), { recursive: true });
  await writeFile(outFile, `${JSON.stringify(manifest, null, 2)}\n`, 'utf8');
}

async function discoverDirectories(root, directory, predicate) {
  const base = join(root, directory);
  if (!(await exists(base))) return [];
  const entries = await readdir(base, { withFileTypes: true });
  return entries
    .filter((entry) => entry.isDirectory())
    .filter((entry) => predicate(entry.name))
    .map((entry) => ({
      name: entry.name,
      path: normalizePath(join(directory, entry.name)),
    }))
    .sort((a, b) => a.name.localeCompare(b.name));
}

async function discoverDocs(root) {
  const base = join(root, 'docs');
  if (!(await exists(base))) return [];
  const files = [];
  await walk(base, async (file) => {
    if (!file.endsWith('.md')) return;
    const rel = normalizePath(relative(root, file));
    if (rel.startsWith('docs/zh/')) return;
    files.push({
      title: await readTitle(file),
      path: rel,
    });
  });
  return files.sort((a, b) => a.path.localeCompare(b.path));
}

async function walk(directory, visitor) {
  const entries = await readdir(directory, { withFileTypes: true });
  for (const entry of entries) {
    const fullPath = join(directory, entry.name);
    if (entry.isDirectory()) {
      await walk(fullPath, visitor);
    } else {
      await visitor(fullPath);
    }
  }
}

async function readTitle(file) {
  const content = await readFile(file, 'utf8');
  const line = content.split(/\r?\n/).find((item) => item.startsWith('# '));
  return line ? line.slice(2).trim() : normalizePath(file).split('/').at(-1);
}

async function exists(path) {
  try {
    await stat(path);
    return true;
  } catch (error) {
    if (error?.code === 'ENOENT') return false;
    throw error;
  }
}

function normalizePath(path) {
  return path.split(sep).join('/');
}

function parseArgs(argv) {
  const args = { root: '.', out: 'docs/experience-hub/data/neo-n4.manifest.json' };
  for (let i = 0; i < argv.length; i += 1) {
    const value = argv[i];
    if (value === '--root') args.root = argv[++i];
    if (value === '--out') args.out = argv[++i];
  }
  return args;
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const args = parseArgs(process.argv.slice(2));
  await writeManifest(args.root, args.out);
}
