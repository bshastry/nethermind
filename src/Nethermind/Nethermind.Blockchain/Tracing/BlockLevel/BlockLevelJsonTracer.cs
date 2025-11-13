// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;
using Nethermind.Core.Specs;
using Nethermind.Evm.Tracing;
using Nethermind.Evm.Tracing.BlockOperations;
using Nethermind.Int256;

namespace Nethermind.Blockchain.Tracing.BlockLevel;

/// <summary>
/// Trace levels controlling which record types are emitted during block execution.
/// Implements the block-level tracing specification extending EIP-3155.
/// </summary>
[Flags]
public enum TraceLevel
{
    /// <summary>No tracing</summary>
    None = 0,

    /// <summary>Block lifecycle events: blockStart, blockEnd</summary>
    BlockLifecycle = 1,

    /// <summary>Transaction events: txStart, txEnd</summary>
    Transactions = 2,

    /// <summary>Pre-execution operations: EIP-4788, EIP-2935</summary>
    PreExecution = 4,

    /// <summary>Post-execution operations: withdrawals, execution requests, block rewards</summary>
    PostExecution = 8,

    /// <summary>Validation operations: header validation, gas accounting</summary>
    Validation = 16,

    /// <summary>EIP-3155 operation-level tracing</summary>
    Operations = 32,

    /// <summary>Trie operations: state root, receipt root, transaction root</summary>
    TrieOperations = 64,

    /// <summary>Minimal: blockStart, txStart, txEnd, blockEnd</summary>
    Minimal = BlockLifecycle | Transactions,

    /// <summary>Standard: Minimal + preExecution + postExecution + validation</summary>
    Standard = Minimal | PreExecution | PostExecution | Validation,

    /// <summary>Full: Standard + operations + trieOperation</summary>
    Full = Standard | Operations | TrieOperations
}

/// <summary>
/// Block-level JSON tracer implementing the EIP block-level tracing specification.
/// Writes JSON Lines format (one JSON object per line) for streaming compatibility.
/// </summary>
public class BlockLevelJsonTracer : BlockTracerBase<object, ITxTracer>, IDisposable
{
    private readonly TextWriter _writer;
    private readonly TraceLevel _traceLevel;
    private readonly bool _ownsWriter;
    private readonly object _writeLock = new();
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string? _overrideForkName;

    private Block? _currentBlock;
    private int _currentTxIndex;
    private Transaction? _currentTransaction;
    private TxReceipt? _currentReceipt;
#pragma warning disable CS0414 // Field is assigned but never used - false positive, field is used in WriteTxStart and WriteTxEnd
    private ulong _cumulativeGasUsed;
#pragma warning restore CS0414
    private bool _disposed;

    /// <summary>
    /// Creates a new block-level JSON tracer.
    /// </summary>
    /// <param name="writer">TextWriter to write JSON Lines output to. If null, writes to Console.Out.</param>
    /// <param name="level">Trace level controlling which record types are emitted.</param>
    /// <param name="ownsWriter">If true, disposes the writer when this tracer is disposed.</param>
    /// <param name="forkName">Optional explicit fork name for the trace. When provided, overrides header-based fork detection. Used by blocktest runner to distinguish Prague from Osaka.</param>
    public BlockLevelJsonTracer(TextWriter? writer = null, TraceLevel level = TraceLevel.Full, bool ownsWriter = false, string? forkName = null)
    {
        _writer = writer ?? Console.Out;
        _traceLevel = level;
        _ownsWriter = ownsWriter;
        _overrideForkName = forkName;

        // Configure JSON serialization options with canonical format
        _jsonOptions = CanonicalFormatHelpers.CreateCanonicalOptions();
    }

    /// <summary>
    /// Gets the current trace level configuration.
    /// </summary>
    public TraceLevel Level => _traceLevel;

    public override bool IsTracingRewards => _traceLevel.HasFlag(TraceLevel.PostExecution);

    public override void ReportReward(Address author, string rewardType, UInt256 rewardValue)
    {
        // Rewards are traced via TracePostExecution(BlockRewardsOperation)
        // This legacy method is not used in the block-level tracing specification
    }

    public override void StartNewBlockTrace(Block block)
    {
        base.StartNewBlockTrace(block);

        _currentBlock = block ?? throw new ArgumentNullException(nameof(block));
        _currentTxIndex = 0;
        _cumulativeGasUsed = 0;

        if (ShouldTrace(TraceLevel.BlockLifecycle))
        {
            WriteBlockStart(block);
        }
    }

    protected override ITxTracer OnStart(Transaction? tx)
    {
        _currentTransaction = tx;
        _currentReceipt = null;

        if (ShouldTrace(TraceLevel.Transactions))
        {
            WriteTxStart(tx, _currentTxIndex);
        }

        // Return appropriate tracer based on whether operations are traced
        if (ShouldTrace(TraceLevel.Operations))
        {
            // TODO: Return an operation-level tracer that integrates with EIP-3155
            // For now, return null tracer as operation tracing requires integration
            // with existing GethLikeTxTracer infrastructure
            return NullTxTracer.Instance;
        }

        return NullTxTracer.Instance;
    }

    protected override object OnEnd(ITxTracer txTracer)
    {
        // This is called when EndTxTrace() (parameterless) is used
        // Write txEnd record without receipt for backwards compatibility
        if (ShouldTrace(TraceLevel.Transactions))
        {
            WriteTxEnd(_currentTxIndex, _currentTransaction, null);
        }

        _currentTxIndex++;
        return new(); // Return empty object as we don't collect results
    }

    /// <summary>
    /// Ends transaction trace with receipt information for complete txEnd record.
    /// </summary>
    public override void EndTxTrace(TxReceipt? receipt)
    {
        _currentReceipt = receipt;

        if (ShouldTrace(TraceLevel.Transactions))
        {
            WriteTxEnd(_currentTxIndex, _currentTransaction, receipt);
        }

        _currentTxIndex++;
        // Note: We don't call base.EndTxTrace(receipt) because that would call
        // the parameterless EndTxTrace() which calls OnEnd(), causing double increment
    }

    public override void EndBlockTrace()
    {
        // Note: We do NOT write blockEnd here because:
        // 1. BlockReceiptsTracer calls this method BEFORE calculating the bloom filter
        // 2. We need both state root AND bloom to be set before writing blockEnd
        // 3. BlockProcessor will call FinalizeBlockTrace() after EndBlockTrace() completes
        base.EndBlockTrace();

        // Flush to ensure all records written so far are flushed
        lock (_writeLock)
        {
            _writer.Flush();
        }
    }

    /// <summary>
    /// Finalizes block trace by writing the blockEnd record.
    /// MUST be called AFTER EndBlockTrace() completes so that:
    /// 1. State root is calculated and set on block header
    /// 2. Bloom filter is calculated and set by BlockReceiptsTracer
    /// 3. Before block hash calculation
    /// </summary>
    public void FinalizeBlockTrace()
    {
        if (ShouldTrace(TraceLevel.BlockLifecycle) && _currentBlock is not null)
        {
            WriteBlockEnd(_currentBlock);
        }

        // Final flush
        lock (_writeLock)
        {
            _writer.Flush();
        }
    }

    public override void TracePreExecution(PreExecutionOperation operation)
    {
        if (operation is null) throw new ArgumentNullException(nameof(operation));

        if (ShouldTrace(TraceLevel.PreExecution))
        {
            WriteRecord("preExecution", operation);
        }
    }

    public override void TracePostExecution(PostExecutionOperation operation)
    {
        if (operation is null) throw new ArgumentNullException(nameof(operation));

        if (ShouldTrace(TraceLevel.PostExecution))
        {
            WriteRecord("postExecution", operation);
        }
    }

    public override void TraceValidation(ValidationOperation operation)
    {
        if (operation is null) throw new ArgumentNullException(nameof(operation));

        if (ShouldTrace(TraceLevel.Validation))
        {
            WriteRecord("validation", operation);
        }
    }

    public override void TraceTrieOperation(TrieOperation operation)
    {
        if (operation is null) throw new ArgumentNullException(nameof(operation));

        if (ShouldTrace(TraceLevel.TrieOperations))
        {
            WriteRecord("trieOperation", operation);
        }
    }

    /// <summary>
    /// Checks if a specific trace level should be emitted based on current configuration.
    /// </summary>
    private bool ShouldTrace(TraceLevel level) => _traceLevel.HasFlag(level);

    /// <summary>
    /// Writes a blockStart record in canonical format.
    /// </summary>
    private void WriteBlockStart(Block block)
    {
        var record = new
        {
            type = "blockStart",
            blockNumber = ToHex(block.Number),
            baseFeePerGas = !block.BaseFeePerGas.IsZero ? ToHex(block.BaseFeePerGas) : null,
            blobGasUsed = block.BlobGasUsed.HasValue ? ToHex(block.BlobGasUsed.Value) : null,
            blockHash = CanonicalFormatHelpers.ToCanonicalHash(block.Hash),
            difficulty = ToHex(block.Difficulty),
            excessBlobGas = block.ExcessBlobGas.HasValue ? ToHex(block.ExcessBlobGas.Value) : null,
            fork = DetermineFork(block),
            gasLimit = ToHex(block.GasLimit),
            miner = CanonicalFormatHelpers.ToCanonicalAddress(block.Beneficiary),
            parentBeaconBlockRoot = block.ParentBeaconBlockRoot is not null ?
                CanonicalFormatHelpers.ToCanonicalHash(block.ParentBeaconBlockRoot) : null,
            parentHash = CanonicalFormatHelpers.ToCanonicalHash(block.ParentHash),
            requestsHash = block.RequestsHash is not null ?
                CanonicalFormatHelpers.ToCanonicalHash(block.RequestsHash) : null,
            timestamp = ToHex(block.Timestamp)
        };

        WriteJsonLine(record);
    }

    /// <summary>
    /// Writes a txStart record in canonical format.
    /// </summary>
    private void WriteTxStart(Transaction? tx, int txIndex)
    {
        if (tx is null) return;

        // Build record with alphabetically ordered fields after type and txIndex
        var record = new
        {
            type = "txStart",
            txIndex = ToHex((ulong)txIndex),
            // Optional: access list (EIP-2930)
            accessList = tx.AccessList is not null && !tx.AccessList.IsEmpty ? tx.AccessList.Select(item => new
            {
                address = CanonicalFormatHelpers.ToCanonicalAddress(item.Address),
                storageKeys = item.StorageKeys.Select(k => ToHex(k)).ToArray()
            }).ToArray() : null,
            // Optional: blob hashes (EIP-4844)
            blobVersionedHashes = tx.BlobVersionedHashes?.Length > 0 ?
                tx.BlobVersionedHashes.Select(h => h is not null ? "0x" + Convert.ToHexString(h).ToLowerInvariant() : null).ToArray() : null,
            // ChainId should always be present for transactions that have it set
            cumulativeGasUsedBefore = ToHex(_cumulativeGasUsed),
            from = CanonicalFormatHelpers.ToCanonicalAddress(tx.SenderAddress),
            gasLimit = ToHex(tx.GasLimit),
            // For legacy/EIP-2930 transactions: output gasPrice
            // For EIP-1559+ transactions: output maxFeePerGas and maxPriorityFeePerGas instead
            gasPrice = !tx.Supports1559 ? ToHex(tx.GasPrice) : null,
            // Optional: EIP-1559 fields (only for EIP-1559+ transactions)
            maxFeePerBlobGas = tx.MaxFeePerBlobGas.HasValue ? ToHex(tx.MaxFeePerBlobGas.Value) : null,
            maxFeePerGas = tx.Supports1559 ? ToHex(tx.DecodedMaxFeePerGas) : null,
            maxPriorityFeePerGas = tx.Supports1559 ? ToHex(tx.GasPrice) : null,
            nonce = ToHex(tx.Nonce),
            to = tx.To is not null ? CanonicalFormatHelpers.ToCanonicalAddress(tx.To) : null,
            txHash = CanonicalFormatHelpers.ToCanonicalHash(tx.Hash),
            txType = ToHex((byte)tx.Type),
            value = ToHex(tx.Value)
        };

        WriteJsonLine(record);
    }

    /// <summary>
    /// Writes a txEnd record with data from transaction receipt in canonical format.
    /// </summary>
    private void WriteTxEnd(int txIndex, Transaction? tx, TxReceipt? receipt)
    {
        if (receipt is null)
        {
            // Fallback to minimal record if receipt not available
            WriteJsonLine(new
            {
                type = "txEnd",
                txIndex = ToHex((ulong)txIndex),
                cumulativeGasUsed = ToHex(_cumulativeGasUsed),
                effectiveGasPrice = tx is not null ? ToHex(tx.GasPrice) : "0x0",
                gasPrice = tx is not null ? ToHex(tx.GasPrice) : "0x0",
                gasUsed = "0x0",
                logsBloom = "0x" + new string('0', 512),
                status = "0x1",
                txHash = tx?.Hash is not null ? CanonicalFormatHelpers.ToCanonicalHash(tx.Hash) : string.Empty
            });
            return;
        }

        // Update cumulative gas used
        _cumulativeGasUsed += (ulong)receipt.GasUsed;

        // Calculate effective gas price
        var effectiveGasPrice = tx?.GasPrice ?? 0;
        if (tx?.MaxFeePerGas > 0)
        {
            // EIP-1559 transaction
            var baseFee = _currentBlock?.BaseFeePerGas ?? 0;
            var maxPriorityFeePerGas = tx.MaxPriorityFeePerGas;
            var priorityFee = UInt256.Min(maxPriorityFeePerGas, tx.MaxFeePerGas - baseFee);
            effectiveGasPrice = baseFee + priorityFee;
        }

        // Build complete txEnd record from receipt with alphabetically ordered fields
        // Note: We do NOT include the 'logs' array here - logs are captured in operation-level traces (EIP-3155)
        // and summarized by logsBloom. Including individual logs in txEnd would duplicate data and cause
        // false positives in differential testing vs geth.
        var record = new
        {
            type = "txEnd",
            txIndex = ToHex((ulong)txIndex),
            // Optional: contract address only if contract deployment
            contractAddress = receipt.ContractAddress is not null ?
                CanonicalFormatHelpers.ToCanonicalAddress(receipt.ContractAddress) : null,
            cumulativeGasUsed = ToHex(receipt.GasUsedTotal),
            gasUsed = ToHex(receipt.GasUsed),
            logsBloom = receipt.Bloom is not null ? "0x" + receipt.Bloom.ToString().ToLowerInvariant() : "0x" + new string('0', 512),
            status = ToHex(receipt.StatusCode),
            txHash = CanonicalFormatHelpers.ToCanonicalHash(receipt.TxHash)
        };

        WriteJsonLine(record);
    }

    /// <summary>
    /// Writes a blockEnd record in canonical format.
    /// </summary>
    private void WriteBlockEnd(Block block)
    {
        var record = new
        {
            type = "blockEnd",
            blockNumber = ToHex(block.Number),
            blockHash = CanonicalFormatHelpers.ToCanonicalHash(block.Hash),
            logsBloom = block.Bloom is not null ? "0x" + block.Bloom.ToString().ToLowerInvariant() : "0x" + new string('0', 512),
            receiptsRoot = CanonicalFormatHelpers.ToCanonicalHash(block.ReceiptsRoot),
            // Optional: requests hash (Prague+)
            requestsHash = block.RequestsHash is not null ?
                CanonicalFormatHelpers.ToCanonicalHash(block.RequestsHash) : null,
            stateRoot = CanonicalFormatHelpers.ToCanonicalHash(block.StateRoot),
            // Optional: total blob gas (Cancun+)
            totalBlobGasUsed = block.BlobGasUsed.HasValue ? ToHex(block.BlobGasUsed.Value) : null,
            totalGasUsed = ToHex(block.GasUsed),
            transactionsRoot = CanonicalFormatHelpers.ToCanonicalHash(block.TxRoot),
            validationResult = "valid", // Would need actual validation result from validation operations
            // Optional: withdrawals root (Shanghai+)
            withdrawalsRoot = block.WithdrawalsRoot is not null ?
                CanonicalFormatHelpers.ToCanonicalHash(block.WithdrawalsRoot) : null
        };

        WriteJsonLine(record);
    }

    /// <summary>
    /// Writes the test end marker as the final JSONL record.
    /// This is required by the blocktest trace specification to signal test completion.
    /// </summary>
    /// <param name="testName">Name of the test</param>
    /// <param name="pass">Whether the test passed</param>
    /// <param name="spec">The fork specification used for the test</param>
    /// <param name="duration">Optional test execution duration</param>
    /// <param name="stateRoot">Optional final state root hash</param>
    /// <param name="error">Optional error message if test failed due to validation error</param>
    public void WriteTestEndMarker(string testName, bool pass, IReleaseSpec spec, TimeSpan? duration, Hash256? stateRoot, string? error = null, ErrorDetails? errorDetails = null, long? lastValidBlock = null)
    {
        // Use SortedDictionary for alphabetical key ordering
        var testEndObj = new SortedDictionary<string, object?>();

        // Add fields in alphabetical order for standardization

        // 1. d (duration - optional)
        if (duration.HasValue)
            testEndObj["d"] = Math.Round(duration.Value.TotalSeconds, 3);

        // 2. error (simple error code string for geth compatibility)
        if (errorDetails is not null)
        {
            testEndObj["error"] = errorDetails.Code;
        }

        // 3. fork (required)
        testEndObj["fork"] = spec.Name ?? "unknown";

        // 4. lastValidBlock (optional - last successfully validated block index)
        if (lastValidBlock.HasValue)
            testEndObj["lastValidBlock"] = lastValidBlock.Value;

        // 5. lastValidStateRoot (optional - final state root or last valid state)
        if (stateRoot is not null)
            testEndObj["lastValidStateRoot"] = CanonicalFormatHelpers.ToCanonicalHash(stateRoot);

        // 6. name (required)
        testEndObj["name"] = testName;

        // 7. pass (required)
        testEndObj["pass"] = pass;

        var endMarker = new { testEnd = testEndObj };

        WriteJsonLine(endMarker);
    }

    /// <summary>
    /// Writes a generic record with type field and data object.
    /// </summary>
    private void WriteRecord(string type, object data)
    {
        if (data is null) return;

        // Create wrapper object with type field
        var wrapper = new Dictionary<string, JsonElement>
        {
            ["type"] = JsonSerializer.SerializeToElement(type, _jsonOptions)
        };

        // Merge data properties into wrapper using JsonElement
        var dataJson = JsonSerializer.Serialize(data, _jsonOptions);
        using var doc = JsonDocument.Parse(dataJson);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                wrapper[property.Name] = property.Value.Clone();
            }
        }

        WriteJsonLine(wrapper);
    }

    /// <summary>
    /// Writes a JSON object as a single line (JSON Lines format).
    /// </summary>
    private void WriteJsonLine(object record)
    {
        lock (_writeLock)
        {
            try
            {
                var json = JsonSerializer.Serialize(record, _jsonOptions);
                _writer.WriteLine(json);
                _writer.Flush(); // Flush immediately for real-time debugging
            }
            catch (Exception ex)
            {
                // Log error but don't throw - tracing failures should not break block processing
                Console.Error.WriteLine($"BlockLevelJsonTracer: Failed to write record: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Converts an unsigned long to hexadecimal string with 0x prefix.
    /// </summary>
    private static string ToHex(ulong value) => $"0x{value:x}";

    /// <summary>
    /// Converts a signed long to hexadecimal string with 0x prefix.
    /// </summary>
    private static string ToHex(long value)
    {
        if (value < 0)
            throw new ArgumentException("Cannot convert negative value to hex", nameof(value));
        return $"0x{value:x}";
    }

    /// <summary>
    /// Converts a UInt256 to hexadecimal string with 0x prefix.
    /// </summary>
    private static string ToHex(UInt256 value) => value.ToHexString(true);

    /// <summary>
    /// Converts a byte to hexadecimal string with 0x prefix.
    /// </summary>
    private static string ToHex(byte value) => $"0x{value:x}";

    /// <summary>
    /// Determines the fork name for a block based on its fields.
    /// This is a simplified heuristic - production code would use spec provider.
    /// </summary>
    private string DetermineFork(Block block)
    {
        // Use explicit fork name if provided (e.g., from blocktest)
        if (_overrideForkName is not null)
            return _overrideForkName;

        // Fallback to heuristic detection
        // This is a simplified implementation
        // Production code should use ISpecProvider to determine the fork
        if (block.RequestsHash is not null) return "prague";
        if (block.ParentBeaconBlockRoot is not null) return "cancun";
        if (block.WithdrawalsRoot is not null) return "shanghai";
        if (!block.BaseFeePerGas.IsZero) return "london";
        return "unknown";
    }

    /// <summary>
    /// Disposes the tracer and optionally the underlying writer.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        lock (_writeLock)
        {
            try
            {
                _writer.Flush();

                if (_ownsWriter)
                {
                    _writer.Dispose();
                }
            }
            catch
            {
                // Suppress disposal errors
            }
            finally
            {
                _disposed = true;
            }
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Finalizer to ensure writer is flushed even if Dispose is not called.
    /// </summary>
    ~BlockLevelJsonTracer()
    {
        Dispose();
    }
}
