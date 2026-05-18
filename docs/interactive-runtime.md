# Interactive Neo N4 Runtime Theater

Open the interactive simulator:

[Launch the Runtime Theater](./interactive-runtime/index.html)

The page animates the main Neo N4 operating flows:

- L1 to L2 deposit
- Batch lifecycle
- Proof aggregation through Neo Gateway
- L2 to L1 withdrawal
- Foreign-chain bridge through watchers and committee proofs
- Challenge and recovery path

It is intentionally static: no build step, no external network dependency, and no wallet connection. The runtime logic lives in `docs/interactive-runtime/simulator.js` and is covered by `node --test tests/interactive-runtime/simulator.test.mjs`.

<iframe src="./interactive-runtime/index.html" title="Neo N4 Runtime Theater" style="width: 100%; min-height: 760px; border: 1px solid #d6e1dc; border-radius: 8px;"></iframe>
