// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Evm.Tracing;
using Nethermind.Evm.Tracing.BlockOperations;

namespace Nethermind.Blockchain.Tracing.BlockLevel;

/// <summary>
/// Wraps execution request processing (EIP-7685) to emit post-execution trace records.
/// This captures deposit, withdrawal, and consolidation requests from the execution layer
/// to the consensus layer with complete field parsing and offset information.
/// </summary>
public class TracingExecutionRequestsProcessor
{
    private readonly IBlockTracer _tracer;

    // Request type constants from EIP-7685
    private const byte RequestTypeDeposit = 0x00;      // EIP-6110
    private const byte RequestTypeWithdrawal = 0x01;   // EIP-7002
    private const byte RequestTypeConsolidation = 0x02; // EIP-7251

    /// <summary>
    /// Initializes a new instance of the <see cref="TracingExecutionRequestsProcessor"/> class.
    /// </summary>
    /// <param name="tracer">The block tracer to emit trace records to.</param>
    public TracingExecutionRequestsProcessor(IBlockTracer tracer)
    {
        _tracer = tracer ?? throw new ArgumentNullException(nameof(tracer));
    }

    /// <summary>
    /// Traces execution requests showing the complete parsing details including field offsets,
    /// system call information, and requests hash calculation.
    /// </summary>
    /// <param name="requests">Array of request byte arrays (each prefixed with request type).</param>
    /// <param name="requestsHash">Hash of all execution requests.</param>
    public void TraceExecutionRequests(
        byte[][]? requests,
        Hash256? requestsHash)
    {
        if (requests is null || requests.Length == 0)
        {
            return;
        }

        var operation = new ExecutionRequestsOperation
        {
            RequestsHash = requestsHash?.ToString() ?? string.Empty,
            HashCalculation = new HashCalculation
            {
                SortedRequests = true,  // EIP-7685 requires sorting by type
                HashMethod = "sha256"
            }
        };

        foreach (var requestBytes in requests)
        {
            if (requestBytes is null || requestBytes.Length == 0)
            {
                continue;
            }

            byte requestType = requestBytes[0];
            var executionRequest = ParseExecutionRequest(requestType, requestBytes);

            if (executionRequest is not null)
            {
                operation.Requests.Add(executionRequest);
            }
        }

        _tracer.TracePostExecution(operation);
    }

    /// <summary>
    /// Parses a single execution request with detailed field parsing.
    /// </summary>
    private ExecutionRequest? ParseExecutionRequest(byte requestType, byte[] requestBytes)
    {
        var request = new ExecutionRequest
        {
            RequestType = $"0x{requestType:x2}"
        };

        switch (requestType)
        {
            case RequestTypeDeposit:
                request.RequestName = "deposit";
                request.Eip = "6110";
                ParseDepositRequest(request, requestBytes);
                break;

            case RequestTypeWithdrawal:
                request.RequestName = "withdrawal";
                request.Eip = "7002";
                ParseWithdrawalRequest(request, requestBytes);
                break;

            case RequestTypeConsolidation:
                request.RequestName = "consolidation";
                request.Eip = "7251";
                ParseConsolidationRequest(request, requestBytes);
                break;

            default:
                request.RequestName = "unknown";
                request.Eip = "unknown";
                break;
        }

        request.Parsing.RawBytes = "0x" + Convert.ToHexString(requestBytes).ToLowerInvariant();

        return request;
    }

    /// <summary>
    /// Parses a deposit request (EIP-6110) with fields:
    /// pubkey (48 bytes), withdrawal_credentials (32 bytes), amount (8 bytes), signature (96 bytes), index (8 bytes)
    /// </summary>
    private void ParseDepositRequest(ExecutionRequest request, byte[] requestBytes)
    {
        if (requestBytes.Length < 193) // 1 (type) + 48 + 32 + 8 + 96 + 8
        {
            return;
        }

        int offset = 1; // Skip request type byte

        // Pubkey: 48 bytes at offset 0 (after type byte)
        request.Parsing.Fields["pubkey"] = new RequestField
        {
            Offset = $"0x{0:x}",
            Length = $"0x{48:x}",
            Value = "0x" + Convert.ToHexString(requestBytes, offset, 48).ToLowerInvariant()
        };
        offset += 48;

        // Withdrawal credentials: 32 bytes
        request.Parsing.Fields["withdrawalCredentials"] = new RequestField
        {
            Offset = $"0x{48:x}",
            Length = $"0x{32:x}",
            Value = "0x" + Convert.ToHexString(requestBytes, offset, 32).ToLowerInvariant()
        };
        offset += 32;

        // Amount: 8 bytes (little-endian uint64 representing Gwei)
        request.Parsing.Fields["amount"] = new RequestField
        {
            Offset = $"0x{80:x}",
            Length = $"0x{8:x}",
            Value = "0x" + Convert.ToHexString(requestBytes, offset, 8).ToLowerInvariant(),
            DecodedGwei = "0x" + Convert.ToHexString(requestBytes, offset, 8).ToLowerInvariant()
        };
        offset += 8;

        // Signature: 96 bytes
        request.Parsing.Fields["signature"] = new RequestField
        {
            Offset = $"0x{88:x}",
            Length = $"0x{96:x}",
            Value = "0x" + Convert.ToHexString(requestBytes, offset, 96).ToLowerInvariant()
        };
        offset += 96;

        // Index: 8 bytes (little-endian uint64)
        if (requestBytes.Length >= offset + 8)
        {
            request.Parsing.Fields["index"] = new RequestField
            {
                Offset = $"0x{184:x}",
                Length = $"0x{8:x}",
                Value = "0x" + Convert.ToHexString(requestBytes, offset, 8).ToLowerInvariant()
            };
        }
    }

    /// <summary>
    /// Parses a withdrawal request (EIP-7002) with fields:
    /// source_address (20 bytes), validator_pubkey (48 bytes), amount (8 bytes)
    /// </summary>
    private void ParseWithdrawalRequest(ExecutionRequest request, byte[] requestBytes)
    {
        if (requestBytes.Length < 77) // 1 (type) + 20 + 48 + 8
        {
            return;
        }

        int offset = 1; // Skip request type byte

        // Source address: 20 bytes
        request.Parsing.Fields["sourceAddress"] = new RequestField
        {
            Offset = $"0x{0:x}",
            Length = $"0x{20:x}",
            Value = "0x" + Convert.ToHexString(requestBytes, offset, 20).ToLowerInvariant()
        };
        offset += 20;

        // Validator pubkey: 48 bytes
        request.Parsing.Fields["validatorPubkey"] = new RequestField
        {
            Offset = $"0x{20:x}",
            Length = $"0x{48:x}",
            Value = "0x" + Convert.ToHexString(requestBytes, offset, 48).ToLowerInvariant()
        };
        offset += 48;

        // Amount: 8 bytes (little-endian uint64 representing Gwei)
        request.Parsing.Fields["amount"] = new RequestField
        {
            Offset = $"0x{68:x}",
            Length = $"0x{8:x}",
            Value = "0x" + Convert.ToHexString(requestBytes, offset, 8).ToLowerInvariant(),
            DecodedGwei = "0x" + Convert.ToHexString(requestBytes, offset, 8).ToLowerInvariant()
        };
    }

    /// <summary>
    /// Parses a consolidation request (EIP-7251) with fields:
    /// source_address (20 bytes), source_pubkey (48 bytes), target_pubkey (48 bytes)
    /// </summary>
    private void ParseConsolidationRequest(ExecutionRequest request, byte[] requestBytes)
    {
        if (requestBytes.Length < 117) // 1 (type) + 20 + 48 + 48
        {
            return;
        }

        int offset = 1; // Skip request type byte

        // Source address: 20 bytes
        request.Parsing.Fields["sourceAddress"] = new RequestField
        {
            Offset = $"0x{0:x}",
            Length = $"0x{20:x}",
            Value = "0x" + Convert.ToHexString(requestBytes, offset, 20).ToLowerInvariant()
        };
        offset += 20;

        // Source pubkey: 48 bytes
        request.Parsing.Fields["sourcePubkey"] = new RequestField
        {
            Offset = $"0x{20:x}",
            Length = $"0x{48:x}",
            Value = "0x" + Convert.ToHexString(requestBytes, offset, 48).ToLowerInvariant()
        };
        offset += 48;

        // Target pubkey: 48 bytes
        request.Parsing.Fields["targetPubkey"] = new RequestField
        {
            Offset = $"0x{68:x}",
            Length = $"0x{48:x}",
            Value = "0x" + Convert.ToHexString(requestBytes, offset, 48).ToLowerInvariant()
        };
    }

    /// <summary>
    /// Traces execution requests with system call information.
    /// This overload includes details about the system contract calls that generated the requests.
    /// </summary>
    /// <param name="requests">Array of request byte arrays.</param>
    /// <param name="requestsHash">Hash of all execution requests.</param>
    /// <param name="systemCalls">Dictionary mapping request type to system call details.</param>
    public void TraceExecutionRequests(
        byte[][]? requests,
        Hash256? requestsHash,
        Dictionary<byte, (Address contractAddress, byte[] input, byte[] output, long gasUsed)>? systemCalls)
    {
        if (requests is null || requests.Length == 0)
        {
            return;
        }

        var operation = new ExecutionRequestsOperation
        {
            RequestsHash = requestsHash?.ToString() ?? string.Empty,
            HashCalculation = new HashCalculation
            {
                SortedRequests = true,
                HashMethod = "sha256"
            }
        };

        foreach (var requestBytes in requests)
        {
            if (requestBytes is null || requestBytes.Length == 0)
            {
                continue;
            }

            byte requestType = requestBytes[0];
            var executionRequest = ParseExecutionRequest(requestType, requestBytes);

            if (executionRequest is not null)
            {
                // Add system call information if available
                if (systemCalls is not null && systemCalls.TryGetValue(requestType, out var systemCall))
                {
                    executionRequest.SystemCall = new Evm.Tracing.BlockOperations.SystemCall
                    {
                        ContractAddress = systemCall.contractAddress,
                        Input = "0x" + Convert.ToHexString(systemCall.input).ToLowerInvariant(),
                        Output = "0x" + Convert.ToHexString(systemCall.output).ToLowerInvariant(),
                        GasUsed = $"0x{systemCall.gasUsed:x}"
                    };
                }

                operation.Requests.Add(executionRequest);
            }
        }

        _tracer.TracePostExecution(operation);
    }
}
