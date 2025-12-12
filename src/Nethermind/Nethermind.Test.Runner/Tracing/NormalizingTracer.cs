// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Evm;
using Nethermind.Evm.Tracing;
using Nethermind.Evm.TransactionProcessing;
using Nethermind.Int256;

namespace Nethermind.Test.Runner.Tracing;

/// <summary>
/// Lightweight tracer that captures EVM execution and feeds normalized entries to a TraceNormalizer.
/// Used for cross-VM corpus validation.
/// </summary>
public class NormalizingTracer : ITxTracer, IDisposable
{
    private readonly TraceNormalizer _normalizer;
    private CanonicalOpLog? _currentOp;

    public NormalizingTracer(TraceNormalizer normalizer)
    {
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
    }

    public bool IsTracingReceipt => false;
    public bool IsTracingActions => false;
    public bool IsTracingOpLevelStorage => false;
    public bool IsTracingMemory => false;
    public bool IsTracingInstructions => true;
    public bool IsTracingRefunds => false;
    public bool IsTracingCode => false;
    public bool IsTracingStack => true;
    public bool IsTracingState => false;
    public bool IsTracingStorage => false;
    public bool IsTracingBlockHash => false;
    public bool IsTracingAccess => false;
    public bool IsTracingFees => false;
    public bool IsTracingLogs => false;
    public bool IsTracing => IsTracingInstructions || IsTracingStack;

    public void StartOperation(int pc, Instruction opcode, long gas, in ExecutionEnvironment env, int codeSection = 0, int functionDepth = 0)
    {
        byte opcodeByte = (byte)opcode;
        _currentOp = new CanonicalOpLog
        {
            Pc = pc + env.CodeInfo.PcOffset(),
            Op = opcodeByte,
            OpName = OpcodeNames.GetOpcodeName(opcodeByte),
            Gas = gas,
            Depth = env.GetGethTraceDepth(),
            Section = codeSection,
            FunctionDepth = functionDepth,
            Stack = []
        };
    }

    public void SetOperationStack(TraceStack stack)
    {
        if (_currentOp is null) return;

        // Capture last 6 stack items as UInt256
        int count = Math.Min(6, stack.Count);
        _currentOp.Stack = new List<UInt256>(count);

        for (int i = 0; i < count; i++)
        {
            _currentOp.Stack.Add(stack.PeekUInt256(i));
        }

        // Feed entry to normalizer
        _normalizer.ProcessLog(_currentOp);
        _currentOp = null;
    }

    /// <summary>
    /// Gets the tracing result after execution completes.
    /// </summary>
    /// <param name="stateRoot">The post-execution state root (0x-prefixed hex)</param>
    /// <returns>Tracing result with hash and metadata</returns>
    public TracingResult GetResult(string stateRoot)
    {
        return _normalizer.FinishWithResult(stateRoot);
    }

    public void MarkAsSuccess(Address recipient, GasConsumed gasSpent, byte[] output, LogEntry[] logs, Hash256? stateRoot = null) { }
    public void MarkAsFailed(Address recipient, GasConsumed gasSpent, byte[] output, string? error, Hash256? stateRoot = null) { }
    public void ReportOperationError(EvmExceptionType error) { }
    public void ReportOperationRemainingGas(long gas) { }
    public void SetOperationMemory(TraceMemory memoryTrace) { }
    public void SetOperationMemorySize(ulong newSize) { }
    public void ReportMemoryChange(long offset, in ReadOnlySpan<byte> data) { }
    public void ReportStorageChange(in ReadOnlySpan<byte> key, in ReadOnlySpan<byte> value) { }
    public void ReportLog(LogEntry log) { }
    public void SetOperationStorage(Address address, UInt256 storageIndex, ReadOnlySpan<byte> newValue, ReadOnlySpan<byte> currentValue) { }
    public void LoadOperationStorage(Address address, UInt256 storageIndex, ReadOnlySpan<byte> value) { }
    public void ReportSelfDestruct(Address address, UInt256 balance, Address refundAddress) { }
    public void ReportBalanceChange(Address address, UInt256? before, UInt256? after) { }
    public void ReportCodeChange(Address address, byte[] before, byte[] after) { }
    public void ReportNonceChange(Address address, UInt256? before, UInt256? after) { }
    public void ReportAccountRead(Address address) { }
    public void ReportStorageChange(in StorageCell storageAddress, byte[] before, byte[] after) { }
    public void ReportStorageRead(in StorageCell storageCell) { }
    public void ReportAction(long gas, UInt256 value, Address from, Address to, ReadOnlyMemory<byte> input, ExecutionType callType, bool isPrecompileCall = false) { }
    public void ReportActionEnd(long gas, ReadOnlyMemory<byte> output) { }
    public void ReportActionError(EvmExceptionType exceptionType) { }
    public void ReportActionRevert(long gas, ReadOnlyMemory<byte> output) { }
    public void ReportActionEnd(long gas, Address deploymentAddress, ReadOnlyMemory<byte> deployedCode) { }
    public void ReportBlockHash(Hash256 blockHash) { }
    public void ReportByteCode(ReadOnlyMemory<byte> byteCode) { }
    public void ReportGasUpdateForVmTrace(long refund, long gasAvailable) { }
    public void ReportRefundForVmTrace(long refund, long gasAvailable) { }
    public void ReportRefund(long refund) { }
    public void ReportExtraGasPressure(long extraGasPressure) { }
    public void ReportAccess(IEnumerable<Address> accessedAddresses, IEnumerable<StorageCell> accessedStorageCells) { }
    public void ReportStackPush(in ReadOnlySpan<byte> stackItem) { }
    public void ReportFees(UInt256 fees, UInt256 burntFees) { }

    public void Dispose() { }
}
