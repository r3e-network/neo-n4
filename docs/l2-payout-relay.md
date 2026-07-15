# L2 payout relay

The external-bridge L2 payout path is asynchronous. `ExternalBridgeEscrow` calls the immutable
`NeoHub.L2PayoutAdapter`; the adapter returns `true` only after it has authenticated the escrow,
validated every argument against the exact committee-signed canonical bytes, and stored the queue
record. It does not claim that the target L2 credit is atomic with the L1 transaction.

## Bound instruction

The relay carries `ExternalMessageHasher.EncodeCanonical` bytes unchanged. Their `Hash256` binds,
in order, the source external chain, destination Neo chain, nonce, direction, foreign sender,
recipient, deadline, source transaction, message type, and payload. For a payout, the canonical
asset-transfer payload additionally binds the foreign asset and minimally encoded amount. The
escrow route binds the mapped Neo asset alongside those signed bytes. The adapter, relay decoder,
and L2 native endpoint independently reject any field/hash mismatch; only pure asset transfers are
supported.

## Durable states

| State | Durable fact | Next side effect |
| --- | --- | --- |
| `Enqueued` | Finalized adapter event and exact instruction are in RocksDB | Build target-L2 transaction |
| `CreditPrepared` | Exact signed target-L2 transaction is write-ahead logged | Broadcast or reconcile it |
| `Credited` | Native receipt stores the message hash and L2 transaction hash | Build L1 acknowledgement |
| `AcknowledgementPrepared` | Exact signed L1 acknowledgement is write-ahead logged | Broadcast or reconcile it |
| `Acknowledged` | Adapter stores the exact L2 transaction hash | None; terminal |
| `Poisoned` | Bounded retries are exhausted with the last error retained | Operator repair |

Every restart reconciles chain state before rebroadcasting. The native endpoint authenticates the
pinned relay account, validates its configured Neo chain and local foreign-to-Neo asset mapping,
stores the receipt before minting through `BridgedNep17`, and consumes `(externalChainId, nonce)`
once. The adapter acknowledgement also requires the pinned relay witness and is exact-hash
idempotent.

## Trust assumptions

- L1 finality and both configured RPC endpoints must report canonical chain state.
- The pinned relay signer controls transaction submission and availability, but cannot alter payout
  fields or mint an unregistered mapping through this endpoint.
- The L2 validator set and r3e Neo core fork must enforce the configured native-contract code.
- A stopped relay delays credit and acknowledgement. It cannot make an enqueue look acknowledged,
  and replay cannot produce a second recipient credit.
