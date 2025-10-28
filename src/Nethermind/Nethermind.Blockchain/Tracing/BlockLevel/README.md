# Block-Level JSON Tracer

This directory contains the implementation of the EIP block-level tracing specification for Nethermind.

## Overview

The `BlockLevelJsonTracer` class implements comprehensive block execution tracing that extends EIP-3155 (transaction-level tracing) to include all consensus-critical operations that occur outside regular transaction execution.

## Features

- **JSON Lines Format**: Outputs one JSON object per line for streaming compatibility
- **Configurable Trace Levels**: Control which record types are emitted
- **Thread-Safe**: Safe for concurrent access during block processing
- **Production-Ready**: Includes error handling, proper disposal, and real-time flushing

## Trace Levels

```csharp
public enum TraceLevel
{
    None = 0,
    BlockLifecycle = 1,      // blockStart, blockEnd
    Transactions = 2,         // txStart, txEnd
    PreExecution = 4,         // Pre-execution system calls (EIP-4788, EIP-2935)
    PostExecution = 8,        // Post-execution operations (withdrawals, requests)
    Validation = 16,          // Validation operations (header, gas accounting)
    Operations = 32,          // EIP-3155 operation-level tracing
    TrieOperations = 64,      // Trie computations (state root, receipt root, etc.)

    Minimal = BlockLifecycle | Transactions,
    Standard = Minimal | PreExecution | PostExecution | Validation,
    Full = Standard | Operations | TrieOperations
}
```

## Usage

### Basic Usage

```csharp
using var writer = new StreamWriter("block_trace.jsonl");
using var tracer = new BlockLevelJsonTracer(writer, TraceLevel.Standard);

// During block processing
tracer.StartNewBlockTrace(block);

// Pre-execution operations
var beaconRootOp = new BeaconRootStorageOperation
{
    Timestamp = "0x12345678",
    ParentBeaconBlockRoot = "0x...",
    ContractAddress = Address.FromHex("0x000F3df6D732807Ef1319fB7B8bB8522d0Beac02"),
    // ... more fields
};
tracer.TracePreExecution(beaconRootOp);

// Transaction processing
foreach (var tx in block.Transactions)
{
    var txTracer = tracer.StartNewTxTrace(tx);
    // Execute transaction...
    tracer.EndTxTrace();
}

// Post-execution operations
var withdrawalsOp = new WithdrawalsOperation
{
    Withdrawals = withdrawalsList,
    TotalWithdrawn = "0x6f05b59d3b20000",
    // ... more fields
};
tracer.TracePostExecution(withdrawalsOp);

// Validation operations
var headerValidation = new HeaderValidationOperation
{
    Rules = validationRules,
    // ... more fields
};
tracer.TraceValidation(headerValidation);

// Trie operations
var stateRootOp = new StateRootOperation
{
    AccountUpdates = accountUpdates,
    FinalStateRoot = "0x...",
    // ... more fields
};
tracer.TraceTrieOperation(stateRootOp);

tracer.EndBlockTrace();
```

### Custom Trace Level

```csharp
// Trace only lifecycle and validation operations
var customLevel = TraceLevel.BlockLifecycle | TraceLevel.Validation;
using var tracer = new BlockLevelJsonTracer(Console.Out, customLevel);
```

### Write to Console

```csharp
// Defaults to Console.Out if no writer provided
using var tracer = new BlockLevelJsonTracer(level: TraceLevel.Minimal);
```

## Output Format

The tracer outputs JSON Lines format (newline-delimited JSON). Each line is a complete JSON object with a `type` field identifying the record type.

Example output:

```jsonl
{"type":"blockStart","blockNumber":"0xf4240","blockHash":"0x...","parentHash":"0x...","timestamp":"0x12345678","gasLimit":"0x1c9c380","baseFeePerGas":"0x3b9aca00","difficulty":"0x0","miner":"0x...","fork":"cancun"}
{"type":"preExecution","operation":"beaconRootStorage","eip":"4788","timestamp":"0x12345678","parentBeaconBlockRoot":"0x...","contractAddress":"0x000F3df6D732807Ef1319fB7B8bB8522d0Beac02","ringBuffer":{"index":"0x4d2","timestampSlot":"0x09a4","rootSlot":"0x09a5"},"storageWrites":[{"slot":"0x09a4","oldValue":"0x00...","newValue":"0x12345678"}],"gasUsed":"0x186a0"}
{"type":"txStart","txIndex":"0x0","txHash":"0x...","txType":"0x2","from":"0x...","to":"0x...","value":"0xde0b6b3a7640000","gasLimit":"0x5208","gasPrice":"0x3b9aca00","nonce":"0x5","chainId":"0x1"}
{"type":"txEnd","txIndex":"0x0","txHash":"0x...","status":"0x1","gasUsed":"0x5208","cumulativeGasUsed":"0x5208"}
{"type":"postExecution","operation":"withdrawals","eip":"4895","withdrawals":[{"index":"0x0","validatorIndex":"0x3039","address":"0x...","amountGwei":"0x773594000","amountWei":"0x6f05b59d3b20000"}],"totalWithdrawn":"0x6f05b59d3b20000"}
{"type":"validation","operation":"headerValidation","rules":[{"rule":"gasLimit","parentGasLimit":"0x1c20000","currentGasLimit":"0x1c25800","valid":true}],"overallResult":"valid"}
{"type":"trieOperation","operation":"stateRoot","trieType":"merklePatricia","accountUpdates":[{"sequenceNumber":"0x0","address":"0x...","accountTriePath":"0x0a...","accountChange":{"balance":{"before":"0xde0b6b3a7640000","after":"0xd94d12e16640000"}}}],"finalStateRoot":"0x...","valid":true}
{"type":"blockEnd","blockNumber":"0xf4240","blockHash":"0x...","stateRoot":"0x...","transactionsRoot":"0x...","receiptsRoot":"0x...","totalGasUsed":"0x17990","validationResult":"valid"}
```

## Record Types

1. **blockStart** - Block processing begins
2. **preExecution** - Pre-execution operations (system calls)
3. **txStart** - Transaction processing begins
4. **operation** - EVM operation (EIP-3155) [NOT YET IMPLEMENTED]
5. **txEnd** - Transaction processing completes
6. **postExecution** - Post-execution operations
7. **validation** - Validation operations
8. **trieOperation** - Merkle trie computations
9. **blockEnd** - Block processing completes

## Implementation Notes

### Current Limitations

1. **Operation-level tracing**: The `Operations` trace level is defined but not fully integrated with EIP-3155 operation tracing. This requires integration with the existing `GethLikeTxTracer` infrastructure.

2. **Transaction execution results**: The `txEnd` records currently emit placeholder values for status, gas used, and cumulative gas. A production implementation would need to track these values during transaction execution.

3. **Fork detection**: The `DetermineFork()` method uses simple heuristics. Production code should use `ISpecProvider` to determine the active fork.

### Thread Safety

The tracer uses a lock around the writer to ensure thread-safe access. All write operations are synchronized.

### Error Handling

Tracing failures are logged to `Console.Error` but do not throw exceptions, ensuring that block processing continues even if tracing fails.

### Disposal

The tracer implements `IDisposable` and includes a finalizer to ensure the writer is flushed even if `Dispose()` is not called. Use `using` statements to ensure proper cleanup.

## See Also

- [EIP Block-Level Tracing Specification](../../../../eip-block-level-tracing.md)
- [EIP-3155: EVM Execution Trace Specification](https://eips.ethereum.org/EIPS/eip-3155)
- [Block Operation Classes](../../../Nethermind.Evm/Tracing/BlockOperations/)
- [IBlockTracer Interface](../../../Nethermind.Evm/Tracing/IBlockTracer.cs)

## Contributing

When adding new operation types or extending the tracer:

1. Ensure all numeric values are serialized as hexadecimal strings with `0x` prefix
2. Use JSON Lines format (one object per line, no indentation)
3. Include a `type` field in all records
4. Check trace level before emitting records
5. Handle nulls appropriately (use `JsonIgnoreCondition.WhenWritingNull`)
6. Add XML documentation for all public members
