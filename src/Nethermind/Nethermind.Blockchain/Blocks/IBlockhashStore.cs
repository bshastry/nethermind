// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Specs;
using Nethermind.Evm.Tracing;

namespace Nethermind.Blockchain.Blocks;

public interface IBlockhashStore
{
    public void ApplyBlockhashStateChanges(BlockHeader blockHeader);
    public void ApplyBlockhashStateChanges(BlockHeader blockHeader, IReleaseSpec spec);

    /// <summary>
    /// Applies block hash state changes and returns the gas consumed.
    /// </summary>
    /// <param name="blockHeader">The block header.</param>
    /// <param name="spec">The release spec.</param>
    /// <param name="tracer">The transaction tracer.</param>
    /// <returns>Gas consumed by the system call.</returns>
    public long ApplyBlockhashStateChangesWithGas(BlockHeader blockHeader, IReleaseSpec spec, ITxTracer tracer);

    public Hash256? GetBlockHashFromState(BlockHeader currentBlockHeader, long requiredBlockNumber);
    public Hash256? GetBlockHashFromState(BlockHeader currentBlockHeader, long requiredBlockNumber, IReleaseSpec spec);
}
