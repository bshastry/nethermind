// SPDX-FileCopyrightText:2023 Demerzel Solutions Limited
// SPDX-License-Identifier:LGPL-3.0-only

using System;
using System.Runtime.CompilerServices;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;
using Nethermind.Core.Specs;
using Nethermind.Crypto;
using Nethermind.Evm.State;
using Nethermind.Evm.Tracing;
using Nethermind.Evm.TransactionProcessing;
using Nethermind.Int256;

[assembly: InternalsVisibleTo("Nethermind.Blockchain.Test")]
[assembly: InternalsVisibleTo("Nethermind.Merge.Plugin.Test")]
namespace Nethermind.Blockchain.Blocks;

public class BlockhashStore : IBlockhashStore
{
    private static readonly byte[] EmptyBytes = [0];
    private const long GasLimit = 30_000_000L;

    private readonly ISpecProvider specProvider;
    private readonly IWorldState worldState;
    private readonly ITransactionProcessor? transactionProcessor;

    public BlockhashStore(ISpecProvider specProvider, IWorldState worldState)
    {
        this.specProvider = specProvider;
        this.worldState = worldState;
        this.transactionProcessor = null;
    }

    public BlockhashStore(ISpecProvider specProvider, IWorldState worldState, ITransactionProcessor transactionProcessor)
    {
        this.specProvider = specProvider;
        this.worldState = worldState;
        this.transactionProcessor = transactionProcessor;
    }

    public void ApplyBlockhashStateChanges(BlockHeader blockHeader)
        => ApplyBlockhashStateChanges(blockHeader, specProvider.GetSpec(blockHeader));

    public void ApplyBlockhashStateChanges(BlockHeader blockHeader, IReleaseSpec spec)
    {
        ApplyBlockhashStateChangesWithGas(blockHeader, spec, NullTxTracer.Instance);
    }

    public long ApplyBlockhashStateChangesWithGas(BlockHeader blockHeader, IReleaseSpec spec, ITxTracer tracer)
    {
        if (!spec.IsEip2935Enabled || blockHeader.IsGenesis || blockHeader.ParentHash is null) return 0;

        Address? eip2935Account = spec.Eip2935ContractAddress ?? Eip2935Constants.BlockHashHistoryAddress;
        if (!worldState.IsContract(eip2935Account)) return 0;

        // If we have a transaction processor, use it to execute the system call and measure gas
        // This matches geth's approach of executing EIP-2935 as a system call
        if (transactionProcessor is not null)
        {
            Hash256 parentBlockHash = blockHeader.ParentHash;
            Transaction transaction = new()
            {
                Value = UInt256.Zero,
                Data = parentBlockHash.Bytes.ToArray(),
                To = eip2935Account,
                SenderAddress = Address.SystemUser,
                GasLimit = GasLimit,
                GasPrice = UInt256.Zero
            };

            transaction.Hash = transaction.CalculateHash();

            transactionProcessor.Execute(transaction, tracer);

            // Return the actual gas used from the transaction execution
            return transaction.SpentGas;
        }

        // Fallback to direct storage write if no transaction processor available
        // This maintains backward compatibility but won't measure gas accurately
        Hash256 parentBlockHash = blockHeader.ParentHash;
        UInt256 parentBlockIndex = new UInt256((ulong)((blockHeader.Number - 1) % spec.Eip2935RingBufferSize));
        StorageCell blockHashStoreCell = new(eip2935Account, parentBlockIndex);
        worldState.Set(blockHashStoreCell, parentBlockHash!.Bytes.WithoutLeadingZeros().ToArray());
        return 0;
    }

    public Hash256? GetBlockHashFromState(BlockHeader currentHeader, long requiredBlockNumber)
        => GetBlockHashFromState(currentHeader, requiredBlockNumber, specProvider.GetSpec(currentHeader));

    public Hash256? GetBlockHashFromState(BlockHeader currentHeader, long requiredBlockNumber, IReleaseSpec spec)
    {
        if (requiredBlockNumber >= currentHeader.Number ||
            requiredBlockNumber + spec.Eip2935RingBufferSize < currentHeader.Number)
        {
            return null;
        }
        UInt256 blockIndex = new UInt256((ulong)(requiredBlockNumber % spec.Eip2935RingBufferSize));
        Address? eip2935Account = spec.Eip2935ContractAddress ?? Eip2935Constants.BlockHashHistoryAddress;
        StorageCell blockHashStoreCell = new(eip2935Account, blockIndex);
        ReadOnlySpan<byte> data = worldState.Get(blockHashStoreCell);
        return data.SequenceEqual(EmptyBytes) ? null : Hash256.FromBytesWithPadding(data);
    }
}
