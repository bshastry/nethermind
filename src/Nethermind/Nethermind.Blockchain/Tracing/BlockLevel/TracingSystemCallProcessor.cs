// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;
using Nethermind.Evm.Tracing;
using Nethermind.Evm.Tracing.BlockOperations;
using Nethermind.Int256;

namespace Nethermind.Blockchain.Tracing.BlockLevel;

/// <summary>
/// Wraps system call execution to emit pre-execution trace records.
/// This captures consensus-critical operations that occur before regular transaction processing,
/// such as EIP-4788 beacon root storage and EIP-2935 historical block hash storage.
/// </summary>
public class TracingSystemCallProcessor
{
    private readonly IBlockTracer _tracer;

    /// <summary>
    /// Initializes a new instance of the <see cref="TracingSystemCallProcessor"/> class.
    /// </summary>
    /// <param name="tracer">The block tracer to emit trace records to.</param>
    public TracingSystemCallProcessor(IBlockTracer tracer)
    {
        _tracer = tracer ?? throw new ArgumentNullException(nameof(tracer));
    }

    /// <summary>
    /// Traces beacon root storage operation (EIP-4788) which stores the parent beacon block root
    /// in a system contract using a ring buffer for cross-layer communication.
    /// </summary>
    /// <param name="blockHeader">The current block header.</param>
    /// <param name="parentBeaconBlockRoot">The parent beacon block root from the consensus layer.</param>
    /// <param name="contractAddress">Address of the beacon roots contract (0x000F3df6D732807Ef1319fB7B8bB8522d0Beac02).</param>
    /// <param name="writes">Storage writes performed (timestamp and root slots).</param>
    /// <param name="gasUsed">Gas consumed by this system call.</param>
    public void TraceBeaconRootStorage(
        BlockHeader blockHeader,
        Hash256 parentBeaconBlockRoot,
        Address contractAddress,
        (UInt256 slot, UInt256 oldValue, UInt256 newValue)[] writes,
        long gasUsed)
    {
        if (blockHeader is null || parentBeaconBlockRoot is null || contractAddress is null)
        {
            return;
        }

        // Calculate ring buffer index: timestamp % 8191
        const int historyBufferLength = 8191;
        ulong timestamp = blockHeader.Timestamp;
        ulong ringBufferIndex = timestamp % historyBufferLength;

        // Per EIP-4788: timestamps in slots 0-8190, roots in slots 8191-16381
        ulong timestampSlot = ringBufferIndex;
        ulong rootSlot = ringBufferIndex + historyBufferLength;

        var operation = new BeaconRootStorageOperation
        {
            Timestamp = $"0x{timestamp:x}",
            ParentBeaconBlockRoot = parentBeaconBlockRoot.ToString(),
            ContractAddress = contractAddress,
            RingBuffer = new BeaconRootRingBuffer
            {
                Index = $"0x{ringBufferIndex:x}",
                TimestampSlot = $"0x{timestampSlot:x}",
                RootSlot = $"0x{rootSlot:x}"
            },
            GasUsed = $"0x{gasUsed:x}"
        };

        // Add storage writes
        if (writes is not null)
        {
            foreach (var (slot, oldValue, newValue) in writes)
            {
                operation.StorageWrites.Add(new StorageWrite
                {
                    Slot = slot.ToHexString(skipLeadingZeros: true),
                    OldValue = CanonicalFormatHelpers.UInt256ToHashHex(oldValue),
                    NewValue = CanonicalFormatHelpers.UInt256ToHashHex(newValue)
                });
            }
        }

        _tracer.TracePreExecution(operation);
    }

    /// <summary>
    /// Traces block hash storage operation (EIP-2935) which stores recent block hashes
    /// in a system contract using a ring buffer for BLOCKHASH opcode access.
    /// </summary>
    /// <param name="blockNumber">The current block number.</param>
    /// <param name="parentHash">The parent block hash to be stored.</param>
    /// <param name="contractAddress">Address of the block hash contract (0x0000000000000000000000000000000000002935).</param>
    /// <param name="write">Storage write performed (parent hash at calculated slot).</param>
    /// <param name="gasUsed">Gas consumed by this system call.</param>
    public void TraceBlockHashStorage(
        long blockNumber,
        Hash256 parentHash,
        Address contractAddress,
        (UInt256 slot, UInt256 oldValue, UInt256 newValue) write,
        long gasUsed)
    {
        if (parentHash is null || contractAddress is null)
        {
            return;
        }

        // Calculate ring buffer index: (blockNumber - 1) % HISTORY_SERVE_WINDOW
        const int historyServeWindow = 8191;
        long parentBlockNumber = blockNumber - 1;
        long ringBufferIndex = parentBlockNumber % historyServeWindow;
        if (ringBufferIndex < 0)
        {
            ringBufferIndex += historyServeWindow;
        }

        var operation = new BlockHashStorageOperation
        {
            BlockNumber = $"0x{blockNumber:x}",
            ParentHash = parentHash.ToString(),
            ContractAddress = contractAddress,
            RingBuffer = new BlockHashRingBuffer
            {
                Index = $"0x{ringBufferIndex:x}",
                Slot = $"0x{ringBufferIndex:x}"
            },
            GasUsed = $"0x{gasUsed:x}"
        };

        // Add the storage write
        operation.StorageWrites.Add(new StorageWrite
        {
            Slot = write.slot.ToHexString(skipLeadingZeros: true),
            OldValue = CanonicalFormatHelpers.UInt256ToHashHex(write.oldValue),
            NewValue = CanonicalFormatHelpers.UInt256ToHashHex(write.newValue)
        });

        _tracer.TracePreExecution(operation);
    }
}
