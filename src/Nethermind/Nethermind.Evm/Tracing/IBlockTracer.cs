// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Collections.Generic;
using Nethermind.Core;
using Nethermind.Evm.Tracing.BlockOperations;
using Nethermind.Int256;

namespace Nethermind.Evm.Tracing
{
    /// <summary>
    /// Tracer for blocks.
    /// </summary>
    /// <remarks>
    /// This tracer should be reusable between blocks. <see cref="StartNewBlockTrace"/> call should reset inner tracer state.
    /// </remarks>
    public interface IBlockTracer
    {
        /// <summary>
        /// Is reward state change traced
        /// </summary>
        /// <remarks>
        /// Controls
        /// - <see cref="ReportReward"/>
        /// </remarks>
        bool IsTracingRewards { get; }

        /// <summary>
        /// Reports rewards for bock.
        /// </summary>
        /// <param name="author">Author/coinbase for reward.</param>
        /// <param name="rewardType">Type of reward.</param>
        /// <param name="rewardValue">Value of reward.</param>
        /// <remarks>Depends on <see cref="IsTracingRewards"/></remarks>
        void ReportReward(Address author, string rewardType, UInt256 rewardValue);

        /// <summary>
        /// Starts a trace for new block.
        /// </summary>
        /// <param name="block">Block to be traced.</param>
        void StartNewBlockTrace(Block block);

        /// <summary>
        /// Starts new transaction trace in a block.
        /// </summary>
        /// <param name="tx">Transaction this trace is started for. Null if it's reward trace (only when <see cref="IsTracingRewards"/> is true).</param>
        /// <returns>Returns tracer for transaction.</returns>
        ITxTracer StartNewTxTrace(Transaction? tx);

        /// <summary>
        /// Ends last transaction trace <see cref="StartNewTxTrace"/>.
        /// </summary>
        void EndTxTrace();

        /// <summary>
        /// Ends last transaction trace with receipt information for enhanced tracing.
        /// </summary>
        /// <param name="receipt">Transaction receipt containing execution results (may be null).</param>
        void EndTxTrace(TxReceipt? receipt)
        {
            // Default implementation delegates to parameterless version
            EndTxTrace();
        }

        /// <summary>
        /// Ends block trace <see cref="StartNewBlockTrace"/>.
        /// </summary>
        void EndBlockTrace();

        /// <summary>
        /// Traces a pre-execution operation (system call before regular transactions).
        /// Examples: EIP-4788 beacon root storage, EIP-2935 block hash storage.
        /// </summary>
        /// <param name="operation">Pre-execution operation details.</param>
        void TracePreExecution(PreExecutionOperation operation);

        /// <summary>
        /// Traces a post-execution operation (occurs after all transactions have executed).
        /// Examples: EIP-4895 withdrawals, EIP-7685 execution requests, block rewards.
        /// </summary>
        /// <param name="operation">Post-execution operation details.</param>
        void TracePostExecution(PostExecutionOperation operation);

        /// <summary>
        /// Traces a validation operation that verifies block correctness.
        /// Examples: header validation, gas accounting, blob gas accounting.
        /// </summary>
        /// <param name="operation">Validation operation details.</param>
        void TraceValidation(ValidationOperation operation);

        /// <summary>
        /// Traces a Merkle/Verkle trie computation.
        /// Examples: state root calculation, receipt root, transaction root, withdrawals root.
        /// </summary>
        /// <param name="operation">Trie operation details.</param>
        void TraceTrieOperation(TrieOperation operation);
    }

    public interface IBlockTracer<out TTrace> : IBlockTracer
    {
        IReadOnlyCollection<TTrace> BuildResult();
    }
}
