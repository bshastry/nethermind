// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Threading;
using Nethermind.Core;
using Nethermind.Evm.Tracing.BlockOperations;
using Nethermind.Int256;

namespace Nethermind.Evm.Tracing
{
    public class CancellationBlockTracer(IBlockTracer innerTracer, CancellationToken token = default) : IBlockTracer
    {
        private bool _isTracingRewards;

        public bool IsTracingRewards
        {
            get => _isTracingRewards || innerTracer.IsTracingRewards;
            set => _isTracingRewards = value;
        }

        public void ReportReward(Address author, string rewardType, UInt256 rewardValue)
        {
            token.ThrowIfCancellationRequested();
            if (innerTracer.IsTracingRewards)
            {
                innerTracer.ReportReward(author, rewardType, rewardValue);
            }
        }

        public void StartNewBlockTrace(Block block)
        {
            token.ThrowIfCancellationRequested();
            innerTracer.StartNewBlockTrace(block);
        }

        public ITxTracer StartNewTxTrace(Transaction? tx)
        {
            token.ThrowIfCancellationRequested();
            return innerTracer.StartNewTxTrace(tx).WithCancellation(token);
        }

        public void EndTxTrace()
        {
            token.ThrowIfCancellationRequested();
            innerTracer.EndTxTrace();
        }

        public void EndBlockTrace()
        {
            innerTracer.EndBlockTrace();
        }

        public void TracePreExecution(PreExecutionOperation operation)
        {
            token.ThrowIfCancellationRequested();
            innerTracer.TracePreExecution(operation);
        }

        public void TracePostExecution(PostExecutionOperation operation)
        {
            token.ThrowIfCancellationRequested();
            innerTracer.TracePostExecution(operation);
        }

        public void TraceValidation(ValidationOperation operation)
        {
            token.ThrowIfCancellationRequested();
            innerTracer.TraceValidation(operation);
        }

        public void TraceTrieOperation(TrieOperation operation)
        {
            token.ThrowIfCancellationRequested();
            innerTracer.TraceTrieOperation(operation);
        }
    }
}
