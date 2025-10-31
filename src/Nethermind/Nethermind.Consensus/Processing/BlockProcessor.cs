// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using Nethermind.Blockchain;
using Nethermind.Blockchain.BeaconBlockRoot;
using Nethermind.Blockchain.Blocks;
using Nethermind.Blockchain.Receipts;
using Nethermind.Blockchain.Tracing;
using Nethermind.Blockchain.Tracing.BlockLevel;
using Nethermind.Consensus.ExecutionRequests;
using Nethermind.Consensus.Rewards;
using Nethermind.Consensus.Validators;
using Nethermind.Consensus.Withdrawals;
using Nethermind.Core;
using Nethermind.Core.Specs;
using Nethermind.Core.Threading;
using Nethermind.Crypto;
using Nethermind.Evm;
using Nethermind.Evm.State;
using Nethermind.Evm.Tracing;
using Nethermind.Evm.Tracing.BlockOperations;
using Nethermind.Int256;
using Nethermind.Logging;
using Nethermind.Specs.Forks;
using Nethermind.State;
using static Nethermind.Consensus.Processing.IBlockProcessor;

namespace Nethermind.Consensus.Processing;

public partial class BlockProcessor(
    ISpecProvider specProvider,
    IBlockValidator blockValidator,
    IRewardCalculator rewardCalculator,
    IBlockTransactionsExecutor blockTransactionsExecutor,
    IWorldState stateProvider,
    IReceiptStorage receiptStorage,
    IBeaconBlockRootHandler beaconBlockRootHandler,
    IBlockhashStore blockHashStore,
    ILogManager logManager,
    IWithdrawalProcessor withdrawalProcessor,
    IExecutionRequestsProcessor executionRequestsProcessor)
    : IBlockProcessor
{
    private readonly ILogger _logger = logManager.GetClassLogger();
    protected readonly WorldStateMetricsDecorator _stateProvider = new WorldStateMetricsDecorator(stateProvider);
    private readonly IReceiptsRootCalculator _receiptsRootCalculator = ReceiptsRootCalculator.Instance;

    /// <summary>
    /// We use a single receipt tracer for all blocks. Internally receipt tracer forwards most of the calls
    /// to any block-specific tracers.
    /// </summary>
    protected BlockReceiptsTracer ReceiptsTracer { get; set; } = new();

    public (Block Block, TxReceipt[] Receipts) ProcessOne(Block suggestedBlock, ProcessingOptions options, IBlockTracer blockTracer, IReleaseSpec spec, CancellationToken token)
    {
        if (_logger.IsTrace) _logger.Trace($"Processing block {suggestedBlock.ToString(Block.Format.Short)} ({options})");

        ApplyDaoTransition(suggestedBlock);
        Block block = PrepareBlockForProcessing(suggestedBlock);
        TxReceipt[] receipts = ProcessBlock(block, blockTracer, options, spec, token);
        ValidateProcessedBlock(suggestedBlock, options, block, receipts);
        if (options.ContainsFlag(ProcessingOptions.StoreReceipts))
        {
            StoreTxReceipts(block, receipts, spec);
        }

        return (block, receipts);
    }

    private void ValidateProcessedBlock(Block suggestedBlock, ProcessingOptions options, Block block, TxReceipt[] receipts)
    {
        if (!options.ContainsFlag(ProcessingOptions.NoValidation) && !blockValidator.ValidateProcessedBlock(block, receipts, suggestedBlock, out string? error))
        {
            if (_logger.IsWarn) _logger.Warn(InvalidBlockHelper.GetMessage(suggestedBlock, "invalid block after processing"));
            throw new InvalidBlockException(suggestedBlock, error);
        }

        // Block is valid, copy the account changes as we use the suggested block not the processed one
        suggestedBlock.AccountChanges = block.AccountChanges;
        suggestedBlock.ExecutionRequests = block.ExecutionRequests;
    }

    private bool ShouldComputeStateRoot(BlockHeader header) =>
        !header.IsGenesis || !specProvider.GenesisStateUnavailable;

    protected virtual TxReceipt[] ProcessBlock(
        Block block,
        IBlockTracer blockTracer,
        ProcessingOptions options,
        IReleaseSpec spec,
        CancellationToken token)
    {
        BlockHeader header = block.Header;

        ReceiptsTracer.SetOtherTracer(blockTracer);
        ReceiptsTracer.StartNewBlockTrace(block);

        blockTransactionsExecutor.SetBlockExecutionContext(new BlockExecutionContext(block.Header, spec));

        StoreBeaconRoot(block, spec, blockTracer);
        ApplyBlockHashStateChanges(block, spec, blockTracer);
        _stateProvider.Commit(spec, commitRoots: false);

        TxReceipt[] receipts = blockTransactionsExecutor.ProcessTransactions(block, options, ReceiptsTracer, token);

        _stateProvider.Commit(spec, commitRoots: false);

        CalculateBlooms(receipts);

        if (spec.IsEip4844Enabled)
        {
            header.BlobGasUsed = BlobGasCalculator.CalculateBlobGas(block.Transactions);
        }

        header.ReceiptsRoot = _receiptsRootCalculator.GetReceiptsRoot(receipts, spec, block.ReceiptsRoot);
        ApplyMinerRewards(block, blockTracer, spec);
        ProcessWithdrawalsWithTracing(block, spec, blockTracer);

        // We need to do a commit here as in _executionRequestsProcessor while executing system transactions
        // we do WorldState.Commit(SystemTransactionReleaseSpec.Instance). In SystemTransactionReleaseSpec
        // Eip158Enabled=false, so we end up persisting empty accounts created while processing withdrawals.
        _stateProvider.Commit(spec, commitRoots: false);

        // FIX 1: Process execution requests (postExecution records)
        // This MUST come before validation and blockEnd
        ProcessExecutionRequestsWithTracing(block, receipts, spec, blockTracer);

        // ===== VALIDATION PHASE =====
        // FIX 1: Emit validation records BEFORE blockEnd
        EmitValidationTracing(block, receipts, spec, blockTracer);

        // ===== FINALIZATION PHASE =====
        // Calculate state root BEFORE EndBlockTrace so it can be included in blockEnd record
        _stateProvider.Commit(spec, commitRoots: true);

        if (BlockchainProcessor.IsMainProcessingThread)
        {
            // Get the accounts that have been changed
            block.AccountChanges = _stateProvider.GetAccountChanges();
        }

        if (ShouldComputeStateRoot(header))
        {
            _stateProvider.RecalculateStateRoot();
            header.StateRoot = _stateProvider.StateRoot;
        }

        // CRITICAL: EndBlockTrace MUST be called before hash calculation!
        // EndBlockTrace sets the block header Bloom filter by accumulating receipt blooms.
        // Now that state root is set, we can finalize tracing with both values.
        // If we call EndBlockTrace after hash calculation, the hash will be wrong!
        ReceiptsTracer.EndBlockTrace();

        // Finalize block-level tracing now that both state root and bloom are set
        FinalizeBlockTrace(blockTracer);

        header.Hash = header.CalculateHash();

        return receipts;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CalculateBlooms(TxReceipt[] receipts)
    {
        ParallelUnbalancedWork.For(
            0,
            receipts.Length,
            ParallelUnbalancedWork.DefaultOptions,
            receipts,
            static (i, receipts) =>
            {
                receipts[i].CalculateBloom();
                return receipts;
            });
    }

    private void StoreBeaconRoot(Block block, IReleaseSpec spec, IBlockTracer blockTracer)
    {
        try
        {
            long gasUsed = 0;

            // FIX: Execute beacon root storage WITHOUT tx-level tracing to avoid state corruption
            // The actual state changes should not be traced at transaction level
            gasUsed = beaconBlockRootHandler.StoreBeaconRootWithGas(block, spec, NullTxTracer.Instance);

            // Emit block-level tracing for beacon root storage if tracer supports it
            if (blockTracer is not null && blockTracer is not NullBlockTracer && spec.IsBeaconBlockRootAvailable)
            {
                BlockHeader header = block.Header;
                if (!header.IsGenesis && header.ParentBeaconBlockRoot is not null)
                {
                    var tracingProcessor = new TracingSystemCallProcessor(blockTracer);

                    // Calculate storage writes for tracing
                    // Ring buffer per EIP-4788: timestamps in slots 0-8190, roots in slots 8191-16381
                    const int historyBufferLength = 8191;
                    ulong timestamp = header.Timestamp;
                    ulong ringBufferIndex = timestamp % historyBufferLength;
                    ulong timestampSlot = ringBufferIndex;
                    ulong rootSlot = ringBufferIndex + historyBufferLength;

                    var writes = new[]
                    {
                        ((UInt256)timestampSlot, UInt256.Zero, (UInt256)timestamp),
                        ((UInt256)rootSlot, UInt256.Zero, new UInt256(header.ParentBeaconBlockRoot.Bytes, true))
                    };

                    Address contractAddress = spec.Eip4788ContractAddress ?? Eip4788Constants.BeaconRootsAddress;
                    tracingProcessor.TraceBeaconRootStorage(header, header.ParentBeaconBlockRoot, contractAddress, writes, gasUsed);
                }
            }
        }
        catch (Exception e)
        {
            if (_logger.IsWarn) _logger.Warn($"Storing beacon block root for block {block.ToString(Block.Format.FullHashAndNumber)} failed: {e}");
        }
    }

    private void ApplyBlockHashStateChanges(Block block, IReleaseSpec spec, IBlockTracer blockTracer)
    {
        BlockHeader header = block.Header;
        long gasUsed = 0;

        // FIX: Apply the actual state changes WITHOUT tx-level tracing to avoid state corruption
        // The actual state changes should not be traced at transaction level
        gasUsed = blockHashStore.ApplyBlockhashStateChangesWithGas(header, spec, NullTxTracer.Instance);

        // Emit block-level tracing for block hash storage if tracer supports it
        if (blockTracer is not null && blockTracer is not NullBlockTracer && spec.IsEip2935Enabled)
        {
            if (!header.IsGenesis && header.ParentHash is not null)
            {
                var tracingProcessor = new TracingSystemCallProcessor(blockTracer);

                // Calculate ring buffer index: (blockNumber - 1) % HISTORY_SERVE_WINDOW
                const int historyServeWindow = 8191;
                long parentBlockNumber = header.Number - 1;
                long ringBufferIndex = parentBlockNumber % historyServeWindow;
                if (ringBufferIndex < 0)
                {
                    ringBufferIndex += historyServeWindow;
                }

                UInt256 slot = (UInt256)(ulong)ringBufferIndex;
                var write = (slot, UInt256.Zero, new UInt256(header.ParentHash.Bytes, true));

                Address contractAddress = spec.Eip2935ContractAddress ?? Eip2935Constants.BlockHashHistoryAddress;
                tracingProcessor.TraceBlockHashStorage(header.Number, header.ParentHash, contractAddress, write, gasUsed);
            }
        }
    }

    private void ProcessWithdrawalsWithTracing(Block block, IReleaseSpec spec, IBlockTracer blockTracer)
    {
        // Track which accounts don't exist before withdrawal processing (for accountsCreated count)
        var accountExistenceBeforeWithdrawals = new System.Collections.Generic.Dictionary<Address, bool>();
        var balancesBeforeWithdrawals = new System.Collections.Generic.Dictionary<Address, UInt256>();

        if (blockTracer is not null && blockTracer is not NullBlockTracer && spec.WithdrawalsEnabled)
        {
            if (block.Withdrawals is not null && block.Withdrawals.Length > 0)
            {
                // Capture account existence and balances BEFORE applying withdrawals
                foreach (var withdrawal in block.Withdrawals)
                {
                    if (withdrawal is not null)
                    {
                        accountExistenceBeforeWithdrawals[withdrawal.Address] = _stateProvider.AccountExists(withdrawal.Address);
                        balancesBeforeWithdrawals[withdrawal.Address] = _stateProvider.GetBalance(withdrawal.Address);
                    }
                }
            }
        }

        // Apply actual withdrawals (this may create new accounts)
        withdrawalProcessor.ProcessWithdrawals(block, spec);

        // Emit block-level tracing for withdrawals if tracer supports it
        if (blockTracer is not null && blockTracer is not NullBlockTracer && spec.WithdrawalsEnabled)
        {
            if (block.Withdrawals is not null && block.Withdrawals.Length > 0)
            {
                var tracingProcessor = new TracingWithdrawalsProcessor(blockTracer);

                // Collect balance changes for each withdrawal
                var balanceChanges = new System.Collections.Generic.Dictionary<Address, (UInt256 before, UInt256 after)>();
                int accountsCreated = 0;
                int emptyAccountsDeleted = 0;

                foreach (var withdrawal in block.Withdrawals)
                {
                    if (withdrawal is not null)
                    {
                        // Get balance before (captured before withdrawal processing)
                        UInt256 beforeBalance = balancesBeforeWithdrawals.TryGetValue(withdrawal.Address, out UInt256 before)
                            ? before
                            : UInt256.Zero;

                        // Get current balance (after withdrawal has been applied)
                        UInt256 afterBalance = _stateProvider.GetBalance(withdrawal.Address);

                        balanceChanges[withdrawal.Address] = (beforeBalance, afterBalance);

                        // Count newly created accounts
                        if (accountExistenceBeforeWithdrawals.TryGetValue(withdrawal.Address, out bool existedBefore) && !existedBefore)
                        {
                            accountsCreated++;
                        }
                    }
                }

                tracingProcessor.TraceWithdrawals(block.Withdrawals, balanceChanges, accountsCreated, emptyAccountsDeleted);
            }
        }
    }

    private void ProcessExecutionRequestsWithTracing(Block block, TxReceipt[] receipts, IReleaseSpec spec, IBlockTracer blockTracer)
    {
        // Apply actual execution requests processing
        executionRequestsProcessor.ProcessExecutionRequests(block, _stateProvider, receipts, spec);

        // Emit block-level tracing if requests are enabled in this fork
        if (blockTracer is null || blockTracer is NullBlockTracer || !spec.RequestsEnabled)
        {
            return;
        }

        var tracingProcessor = new TracingExecutionRequestsProcessor(blockTracer);

        // FIX 2: Emit trace even if no requests (per EIP specification)
        if (block.ExecutionRequests is not null && block.ExecutionRequests.Length > 0)
        {
            // Trace execution requests with the calculated hash
            tracingProcessor.TraceExecutionRequests(block.ExecutionRequests, block.Header.RequestsHash);
        }
        else
        {
            // Emit empty execution requests record (required by EIP)
            var emptyOperation = new ExecutionRequestsOperation
            {
                Operation = "executionRequests",
                Eip = "7685",
                RequestsHash = block.Header.RequestsHash?.ToString() ?? string.Empty,
                HashCalculation = new HashCalculation
                {
                    SortedRequests = true,
                    HashMethod = "sha256"
                },
                Requests = new List<ExecutionRequest>() // Empty list
            };

            blockTracer.TracePostExecution(emptyOperation);
        }
    }

    private void EmitValidationTracing(Block block, TxReceipt[] receipts, IReleaseSpec spec, IBlockTracer blockTracer)
    {
        if (blockTracer is null || blockTracer is NullBlockTracer)
        {
            return;
        }

        BlockHeader header = block.Header;

        // Trace gas accounting
        var gasValidator = new TracingGasValidator(blockTracer);

        // Calculate gas used for each transaction from receipts
        var gasUsed = new long[receipts.Length];
        for (int i = 0; i < receipts.Length; i++)
        {
            gasUsed[i] = receipts[i].GasUsed;
        }

        bool gasValid = true;
        long totalGas = 0;
        foreach (var gas in gasUsed)
        {
            totalGas += gas;
            if (totalGas > header.GasLimit)
            {
                gasValid = false;
                break;
            }
        }

        gasValidator.TraceGasAccounting(block.Transactions, gasUsed, header.GasLimit, gasValid);

        // Trace blob gas accounting if EIP-4844 is enabled
        if (spec.IsEip4844Enabled)
        {
            var blobTxsWithIndices = new System.Collections.Generic.List<(Transaction tx, int originalIndex)>();
            for (int i = 0; i < block.Transactions.Length; i++)
            {
                var tx = block.Transactions[i];
                if (tx.Type == TxType.Blob)
                {
                    blobTxsWithIndices.Add((tx, i));
                }
            }

            ulong excessBlobGas = header.ExcessBlobGas ?? 0;
            BlobGasCalculator.TryCalculateFeePerBlobGas(excessBlobGas, spec.BlobBaseFeeUpdateFraction, out UInt256 blobGasPrice);
            long maxBlobGasPerBlock = (long)(spec.MaxBlobCount * Nethermind.Core.Eip4844Constants.GasPerBlob);
            bool blobGasValid = true;
            long totalBlobGas = 0;

            if (blobTxsWithIndices.Count > 0)
            {
                foreach (var (tx, _) in blobTxsWithIndices)
                {
                    int blobCount = tx.BlobVersionedHashes?.Length ?? 0;
                    totalBlobGas += blobCount * 131072; // GAS_PER_BLOB
                }

                if (totalBlobGas > maxBlobGasPerBlock)
                {
                    blobGasValid = false;
                }
            }

            var blobTxsArray = blobTxsWithIndices.Count > 0 ? blobTxsWithIndices.Select(x => x.tx).ToArray() : null;
            var originalIndicesArray = blobTxsWithIndices.Count > 0 ? blobTxsWithIndices.Select(x => x.originalIndex).ToArray() : null;
            gasValidator.TraceBlobGasAccounting(blobTxsArray, excessBlobGas, blobGasPrice, blobGasValid, maxBlobGasPerBlock, originalIndicesArray, spec.BlobBaseFeeUpdateFraction);
        }
    }

    private static void FinalizeBlockTrace(IBlockTracer blockTracer)
    {
        if (blockTracer is null || blockTracer is NullBlockTracer)
        {
            return;
        }

        // If the tracer is a BlockLevelJsonTracer, call FinalizeBlockTrace to write blockEnd
        // with both state root and bloom filter set
        if (blockTracer is Blockchain.Tracing.BlockLevel.BlockLevelJsonTracer jsonTracer)
        {
            jsonTracer.FinalizeBlockTrace();
        }
        else if (blockTracer is BlockReceiptsTracer receiptsTracer && receiptsTracer.WrappedBlockTracer is not null)
        {
            // The receipts tracer wraps another tracer - finalize that one
            FinalizeBlockTrace(receiptsTracer.WrappedBlockTracer);
        }
        // Note: CompositeBlockTracer support could be added if needed by exposing _childTracers
    }

    private void StoreTxReceipts(Block block, TxReceipt[] txReceipts, IReleaseSpec spec)
    {
        // Setting canonical is done when the BlockAddedToMain event is fired
        receiptStorage.Insert(block, txReceipts, spec, false);
    }

    private Block PrepareBlockForProcessing(Block suggestedBlock)
    {
        if (_logger.IsTrace) _logger.Trace($"{suggestedBlock.Header.ToString(BlockHeader.Format.Full)}");
        BlockHeader bh = suggestedBlock.Header;
        BlockHeader headerForProcessing = new(
            bh.ParentHash,
            bh.UnclesHash,
            bh.Beneficiary,
            bh.Difficulty,
            bh.Number,
            bh.GasLimit,
            bh.Timestamp,
            bh.ExtraData,
            bh.BlobGasUsed,
            bh.ExcessBlobGas)
        {
            Bloom = Bloom.Empty,
            Author = bh.Author,
            Hash = bh.Hash,
            MixHash = bh.MixHash,
            Nonce = bh.Nonce,
            TxRoot = bh.TxRoot,
            TotalDifficulty = bh.TotalDifficulty,
            AuRaStep = bh.AuRaStep,
            AuRaSignature = bh.AuRaSignature,
            ReceiptsRoot = bh.ReceiptsRoot,
            BaseFeePerGas = bh.BaseFeePerGas,
            WithdrawalsRoot = bh.WithdrawalsRoot,
            RequestsHash = bh.RequestsHash,
            IsPostMerge = bh.IsPostMerge,
            ParentBeaconBlockRoot = bh.ParentBeaconBlockRoot
        };

        if (!ShouldComputeStateRoot(bh))
        {
            headerForProcessing.StateRoot = bh.StateRoot;
        }

        return suggestedBlock.WithReplacedHeader(headerForProcessing);
    }

    private void ApplyMinerRewards(Block block, IBlockTracer tracer, IReleaseSpec spec)
    {
        if (_logger.IsTrace) _logger.Trace("Applying miner rewards:");
        BlockReward[] rewards = rewardCalculator.CalculateRewards(block);
        for (int i = 0; i < rewards.Length; i++)
        {
            BlockReward reward = rewards[i];

            using ITxTracer txTracer = tracer.IsTracingRewards
                ? // we need this tracer to be able to track any potential miner account creation
                tracer.StartNewTxTrace(null)
                : NullTxTracer.Instance;

            ApplyMinerReward(block, reward, spec);

            if (tracer.IsTracingRewards)
            {
                tracer.EndTxTrace();
                tracer.ReportReward(reward.Address, reward.RewardType.ToLowerString(), reward.Value);
                if (txTracer.IsTracingState)
                {
                    _stateProvider.Commit(spec, txTracer);
                }
            }
        }
    }

    private void ApplyMinerReward(Block block, BlockReward reward, IReleaseSpec spec)
    {
        if (_logger.IsTrace) _logger.Trace($"  {(BigInteger)reward.Value / (BigInteger)Unit.Ether:N3}{Unit.EthSymbol} for account at {reward.Address}");

        _stateProvider.AddToBalanceAndCreateIfNotExists(reward.Address, reward.Value, spec);
    }

    private void ApplyDaoTransition(Block block)
    {
        long? daoBlockNumber = specProvider.DaoBlockNumber;
        if (daoBlockNumber.HasValue && daoBlockNumber.Value == block.Header.Number)
        {
            ApplyTransition();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        void ApplyTransition()
        {
            if (_logger.IsInfo) _logger.Info("Applying the DAO transition");
            Address withdrawAccount = DaoData.DaoWithdrawalAccount;
            if (!_stateProvider.AccountExists(withdrawAccount))
            {
                _stateProvider.CreateAccount(withdrawAccount, 0);
            }

            foreach (Address daoAccount in DaoData.DaoAccounts)
            {
                UInt256 balance = _stateProvider.GetBalance(daoAccount);
                _stateProvider.AddToBalance(withdrawAccount, balance, Dao.Instance);
                _stateProvider.SubtractFromBalance(daoAccount, balance, Dao.Instance);
            }
        }
    }
}
