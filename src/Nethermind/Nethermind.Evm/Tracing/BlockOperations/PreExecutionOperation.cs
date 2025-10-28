// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Collections.Generic;
using System.Text.Json.Serialization;
using Nethermind.Core;
using Nethermind.Int256;

namespace Nethermind.Evm.Tracing.BlockOperations;

/// <summary>
/// Base class for pre-execution operations that occur before regular transaction processing.
/// These include system calls like EIP-4788 (beacon root storage) and EIP-2935 (block hash storage).
/// </summary>
public abstract class PreExecutionOperation
{
    /// <summary>
    /// Type of pre-execution operation (e.g., "beaconRootStorage", "blockHashStorage")
    /// </summary>
    [JsonPropertyName("operation")]
    public string Operation { get; set; } = string.Empty;

    /// <summary>
    /// EIP number that defines this operation (e.g., "4788", "2935")
    /// </summary>
    [JsonPropertyName("eip")]
    public string Eip { get; set; } = string.Empty;

    /// <summary>
    /// Gas consumed by this system call (hexadecimal format)
    /// </summary>
    [JsonPropertyName("gasUsed")]
    public string GasUsed { get; set; } = "0x0";

    /// <summary>
    /// Address of the system contract being called
    /// </summary>
    [JsonPropertyName("contractAddress")]
    public Address? ContractAddress { get; set; }

    /// <summary>
    /// Storage writes performed by this operation
    /// </summary>
    [JsonPropertyName("storageWrites")]
    public List<StorageWrite> StorageWrites { get; set; } = new();
}

/// <summary>
/// Represents a single storage write operation with slot and value information
/// </summary>
public class StorageWrite
{
    /// <summary>
    /// Storage slot being written (hexadecimal)
    /// </summary>
    [JsonPropertyName("slot")]
    public string Slot { get; set; } = string.Empty;

    /// <summary>
    /// Previous value at this storage slot (hexadecimal)
    /// </summary>
    [JsonPropertyName("oldValue")]
    public string OldValue { get; set; } = string.Empty;

    /// <summary>
    /// New value being written to this storage slot (hexadecimal)
    /// </summary>
    [JsonPropertyName("newValue")]
    public string NewValue { get; set; } = string.Empty;
}

/// <summary>
/// Beacon root storage operation (EIP-4788) that stores the parent beacon block root
/// in a system contract for cross-layer communication.
/// </summary>
public class BeaconRootStorageOperation : PreExecutionOperation
{
    /// <summary>
    /// Block timestamp used for ring buffer indexing
    /// </summary>
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    /// <summary>
    /// Parent beacon block root from the consensus layer
    /// </summary>
    [JsonPropertyName("parentBeaconBlockRoot")]
    public string ParentBeaconBlockRoot { get; set; } = string.Empty;

    /// <summary>
    /// Ring buffer indexing information for circular storage
    /// </summary>
    [JsonPropertyName("ringBuffer")]
    public BeaconRootRingBuffer RingBuffer { get; set; } = new();

    public BeaconRootStorageOperation()
    {
        Operation = "beaconRootStorage";
        Eip = "4788";
    }
}

/// <summary>
/// Ring buffer information for beacon root storage using circular indexing
/// </summary>
public class BeaconRootRingBuffer
{
    /// <summary>
    /// Calculated ring buffer index (timestamp % 8191)
    /// </summary>
    [JsonPropertyName("index")]
    public string Index { get; set; } = string.Empty;

    /// <summary>
    /// Storage slot for timestamp (index, per EIP-4788)
    /// </summary>
    [JsonPropertyName("timestampSlot")]
    public string TimestampSlot { get; set; } = string.Empty;

    /// <summary>
    /// Storage slot for root (index + 8191, per EIP-4788)
    /// </summary>
    [JsonPropertyName("rootSlot")]
    public string RootSlot { get; set; } = string.Empty;
}

/// <summary>
/// Historical block hash storage operation (EIP-2935) that stores recent block hashes
/// in a system contract for BLOCKHASH opcode access.
/// </summary>
public class BlockHashStorageOperation : PreExecutionOperation
{
    /// <summary>
    /// Current block number
    /// </summary>
    [JsonPropertyName("blockNumber")]
    public string BlockNumber { get; set; } = string.Empty;

    /// <summary>
    /// Parent block hash to be stored
    /// </summary>
    [JsonPropertyName("parentHash")]
    public string ParentHash { get; set; } = string.Empty;

    /// <summary>
    /// Ring buffer indexing information for circular storage
    /// </summary>
    [JsonPropertyName("ringBuffer")]
    public BlockHashRingBuffer RingBuffer { get; set; } = new();

    public BlockHashStorageOperation()
    {
        Operation = "blockHashStorage";
        Eip = "2935";
    }
}

/// <summary>
/// Ring buffer information for block hash storage using circular indexing
/// </summary>
public class BlockHashRingBuffer
{
    /// <summary>
    /// Calculated ring buffer index ((blockNumber - 1) % HISTORY_SERVE_WINDOW)
    /// </summary>
    [JsonPropertyName("index")]
    public string Index { get; set; } = string.Empty;

    /// <summary>
    /// Storage slot for this block's hash
    /// </summary>
    [JsonPropertyName("slot")]
    public string Slot { get; set; } = string.Empty;
}
