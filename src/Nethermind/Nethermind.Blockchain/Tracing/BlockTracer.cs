// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Core;
using Nethermind.Evm.Tracing;
using Nethermind.Evm.Tracing.BlockOperations;
using Nethermind.Int256;

namespace Nethermind.Blockchain.Tracing;

public abstract class BlockTracer : IBlockTracer
{
    public virtual bool IsTracingRewards => false;
    public virtual void ReportReward(Address author, string rewardType, UInt256 rewardValue) { }
    public virtual void StartNewBlockTrace(Block block) { }
    public abstract ITxTracer StartNewTxTrace(Transaction? tx);
    public virtual void EndTxTrace() { }
    public virtual void EndBlockTrace() { }
    public virtual void TracePreExecution(PreExecutionOperation operation) { }
    public virtual void TracePostExecution(PostExecutionOperation operation) { }
    public virtual void TraceValidation(ValidationOperation operation) { }
    public virtual void TraceTrieOperation(TrieOperation operation) { }
}
