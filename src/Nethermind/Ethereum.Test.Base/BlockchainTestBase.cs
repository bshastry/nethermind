// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Nethermind.Blockchain;
using Nethermind.Blockchain.Find;
using Nethermind.Config;
using Nethermind.Consensus;
using Nethermind.Consensus.Ethash;
using Nethermind.Consensus.Processing;
using Nethermind.Consensus.Rewards;
using Nethermind.Consensus.Validators;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;
using Nethermind.Core.Specs;
using Nethermind.Core.Test.Modules;
using Nethermind.Crypto;
using Nethermind.Int256;
using Nethermind.Logging;
using Nethermind.Serialization.Rlp;
using Nethermind.Specs;
using Nethermind.Specs.Forks;
using Nethermind.Specs.Test;
using Nethermind.Evm.State;
using Nethermind.Init.Modules;
using NUnit.Framework;
using Nethermind.Merge.Plugin.Data;
using Nethermind.Merge.Plugin;
using Nethermind.JsonRpc;
using System.Reflection;

namespace Ethereum.Test.Base;

public abstract class BlockchainTestBase
{
    private static readonly ILogger _logger;
    private static readonly ILogManager _logManager = new TestLogManager(LogLevel.Warn);
    private static DifficultyCalculatorWrapper DifficultyCalculator { get; }
    private const int _genesisProcessingTimeoutMs = 5000;

    static BlockchainTestBase()
    {
        DifficultyCalculator = new DifficultyCalculatorWrapper();
        _logManager ??= LimboLogs.Instance;
        _logger = _logManager.GetClassLogger();
    }

    [SetUp]
    public void Setup()
    {
    }

    private class DifficultyCalculatorWrapper : IDifficultyCalculator
    {
        public IDifficultyCalculator? Wrapped { get; set; }

        public UInt256 Calculate(BlockHeader header, BlockHeader parent)
        {
            if (Wrapped is null)
            {
                throw new InvalidOperationException(
                    $"Cannot calculate difficulty before the {nameof(Wrapped)} calculator is set.");
            }

            return Wrapped.Calculate(header, parent);
        }
    }

    protected async Task<EthereumTestResult> RunTest(BlockchainTest test, Stopwatch? stopwatch = null, bool failOnInvalidRlp = true, ITestBlockTracer? tracer = null)
    {
        _logger.Info($"Running {test.Name}, Network: [{test.Network!.Name}] at {DateTime.UtcNow:HH:mm:ss.ffffff}");
        if (test.NetworkAfterTransition is not null)
            _logger.Info($"Network after transition: [{test.NetworkAfterTransition.Name}] at {test.TransitionForkActivation}");
        Assert.That(test.LoadFailure, Is.Null, "test data loading failure");

        test.Network = ChainUtils.ResolveSpec(test.Network, test.ChainId);
        test.NetworkAfterTransition = ChainUtils.ResolveSpec(test.NetworkAfterTransition, test.ChainId);

        bool isEngineTest = test.Blocks is null && test.EngineNewPayloads is not null;

        List<(ForkActivation Activation, IReleaseSpec Spec)> transitions =
            isEngineTest ?
            [((ForkActivation)0, test.Network)] :
            [((ForkActivation)0, test.GenesisSpec), ((ForkActivation)1, test.Network)]; // TODO: this thing took a lot of time to find after it was removed!, genesis block is always initialized with Frontier

        if (test.NetworkAfterTransition is not null)
        {
            transitions.Add((test.TransitionForkActivation!.Value, test.NetworkAfterTransition));
        }

        ISpecProvider specProvider = new CustomSpecProvider(test.ChainId, test.ChainId, transitions.ToArray());

        Assert.That(isEngineTest || test.ChainId == GnosisSpecProvider.Instance.ChainId || specProvider.GenesisSpec == Frontier.Instance, "Expected genesis spec to be Frontier for blockchain tests");

        if (test.Network is Cancun || test.NetworkAfterTransition is Cancun)
        {
            await KzgPolynomialCommitments.InitializeAsync();
        }

        DifficultyCalculator.Wrapped = new EthashDifficultyCalculator(specProvider);
        IRewardCalculator rewardCalculator = new RewardCalculator(specProvider);
        bool isPostMerge = test.Network != London.Instance &&
                           test.Network != Berlin.Instance &&
                           test.Network != MuirGlacier.Instance &&
                           test.Network != Istanbul.Instance &&
                           test.Network != ConstantinopleFix.Instance &&
                           test.Network != Constantinople.Instance &&
                           test.Network != Byzantium.Instance &&
                           test.Network != SpuriousDragon.Instance &&
                           test.Network != TangerineWhistle.Instance &&
                           test.Network != Dao.Instance &&
                           test.Network != Homestead.Instance &&
                           test.Network != Frontier.Instance &&
                           test.Network != Olympic.Instance;
        if (isPostMerge)
        {
            rewardCalculator = NoBlockRewards.Instance;
            specProvider.UpdateMergeTransitionInfo(0, 0);
        }

        IConfigProvider configProvider = new ConfigProvider();
        // configProvider.GetConfig<IBlocksConfig>().PreWarmStateOnBlockProcessing = false;
        // When tracing, redirect logs to stdout to avoid polluting stderr (where JSON traces go)
        ILogManager logManager = tracer is not null
            ? new TestLogManager(LogLevel.Warn, useStdout: true)
            : _logManager;

        ContainerBuilder containerBuilder = new ContainerBuilder()
            .AddModule(new TestNethermindModule(configProvider))
            .AddSingleton(specProvider)
            .AddSingleton(logManager)
            .AddSingleton(rewardCalculator)
            .AddSingleton<IDifficultyCalculator>(DifficultyCalculator);

        if (isEngineTest)
        {
            containerBuilder.AddModule(new TestMergeModule(configProvider));
        }

        await using IContainer container = containerBuilder.Build();

        IMainProcessingContext mainBlockProcessingContext = container.Resolve<IMainProcessingContext>();
        IWorldState stateProvider = mainBlockProcessingContext.WorldState;
        BlockchainProcessor blockchainProcessor = (BlockchainProcessor)mainBlockProcessingContext.BlockchainProcessor;
        IBlockTree blockTree = container.Resolve<IBlockTree>();
        IBlockValidator blockValidator = container.Resolve<IBlockValidator>();
        blockchainProcessor.Start();

        // Register tracer if provided for blocktest tracing
        if (tracer is not null)
        {
            blockchainProcessor.Tracers.Add(tracer);
        }

        try
        {
            BlockHeader parentHeader;
            // Genesis processing
            using (stateProvider.BeginScope(null))
            {
                InitializeTestState(test, stateProvider, specProvider);

                stopwatch?.Start();

                test.GenesisRlp ??= Rlp.Encode(new Block(JsonToEthereumTest.Convert(test.GenesisBlockHeader)));

                Block genesisBlock = Rlp.Decode<Block>(test.GenesisRlp.Bytes);
                Assert.That(genesisBlock.Header.Hash, Is.EqualTo(new Hash256(test.GenesisBlockHeader.Hash)));

                ManualResetEvent genesisProcessed = new(false);

                blockTree.NewHeadBlock += (_, args) =>
                {
                    if (args.Block.Number == 0)
                    {
                        Assert.That(stateProvider.HasStateForBlock(genesisBlock.Header), Is.True);
                        genesisProcessed.Set();
                    }
                };

                blockchainProcessor.BlockRemoved += (_, args) =>
                {
                    if (args.ProcessingResult != ProcessingResult.Success && args.BlockHash == genesisBlock.Header.Hash)
                    {
                        Assert.Fail($"Failed to process genesis block: {args.Exception}");
                        genesisProcessed.Set();
                    }
                };

                blockTree.SuggestBlock(genesisBlock);
                genesisProcessed.WaitOne(_genesisProcessingTimeoutMs);
                parentHeader = genesisBlock.Header;

                // Dispose genesis block's AccountChanges
                genesisBlock.DisposeAccountChanges();
            }

            string? validationError = null;
            long lastValidBlockIndex = 0;
            if (test.Blocks is not null)
            {
                // blockchain test
                (parentHeader, validationError, lastValidBlockIndex) = SuggestBlocks(test, failOnInvalidRlp, blockValidator, blockTree, parentHeader);
            }
            else if (test.EngineNewPayloads is not null)
            {
                // engine test
                IEngineRpcModule engineRpcModule = container.Resolve<IEngineRpcModule>();
                await RunNewPayloads(test.EngineNewPayloads, engineRpcModule);
            }
            else
            {
                Assert.Fail("Invalid blockchain test, did not contain blocks or new payloads.");
            }

            // NOTE: Tracer removal must happen AFTER StopAsync to ensure all blocks are traced
            // Blocks are queued asynchronously, so we need to wait for processing to complete
            await blockchainProcessor.StopAsync(true);
            stopwatch?.Stop();

            IBlockCachePreWarmer? preWarmer = container.Resolve<MainProcessingContext>().LifetimeScope.ResolveOptional<IBlockCachePreWarmer>();

            // Caches are cleared async, which is a problem as read for the MainWorldState with prewarmer is not correct if its not cleared.
            preWarmer?.ClearCaches();

            Block? headBlock = blockTree.RetrieveHeadBlock();

            Assert.That(headBlock, Is.Not.Null);
            if (headBlock is null)
            {
                return new EthereumTestResult(test.Name, null, false);
            }

            List<string> differences = new();

            // Only run state assertions if there was no validation error
            // (invalid blocks cause different final state than expected)
            if (validationError is null)
            {
                using (stateProvider.BeginScope(headBlock.Header))
                {
                    differences = RunAssertions(test, headBlock, stateProvider);
                }
            }

            bool testPassed = differences.Count == 0 && validationError is null;

            // Write test end marker if using streaming tracer (JSONL format)
            // This must be done BEFORE removing tracer and BEFORE Assert to ensure marker is written even on failure
            if (tracer is not null)
            {
                // Map error to EEST canonical format
                ErrorDetails? errorDetails = null;
                if (validationError is not null)
                {
                    errorDetails = EestErrorMapper.MapErrorToEEST(validationError);
                }

                tracer.TestFinished(
                    test.Name,
                    testPassed,
                    test.Network,
                    stopwatch?.Elapsed,
                    headBlock?.StateRoot,
                    validationError,
                    errorDetails,
                    testPassed ? null : lastValidBlockIndex);
                blockchainProcessor.Tracers.Remove(tracer);
            }

            // Only assert on state differences if there was no validation error
            if (validationError is null)
            {
                Assert.That(differences, Is.Empty, "differences");
            }

            return new EthereumTestResult(test.Name, null, testPassed);
        }
        catch (Exception)
        {
            await blockchainProcessor.StopAsync(true);
            throw;
        }
    }

    private static (BlockHeader parentHeader, string? error, long lastValidBlock) SuggestBlocks(BlockchainTest test, bool failOnInvalidRlp, IBlockValidator blockValidator, IBlockTree blockTree, BlockHeader parentHeader)
    {
        List<(Block Block, string ExpectedException)> correctRlp = DecodeRlps(test, failOnInvalidRlp);
        for (int i = 0; i < correctRlp.Count; i++)
        {
            // Mimic the actual behaviour where block goes through validating sync manager
            correctRlp[i].Block.Header.IsPostMerge = correctRlp[i].Block.Difficulty == 0;

            // For tests with reorgs, find the actual parent header from block tree
            BlockHeader? currentParentHeader = blockTree.FindHeader(correctRlp[i].Block.ParentHash);

            bool expectsException = correctRlp[i].ExpectedException is not null;

            // Check if parent exists (unless this is genesis block)
            if (currentParentHeader is null && correctRlp[i].Block.Number > 0)
            {
                if (!expectsException)
                {
                    // Block has missing parent and shouldn't fail → test failed
                    return (parentHeader,
                        $"block #{correctRlp[i].Block.Number} has unknown parent {correctRlp[i].Block.ParentHash}",
                        parentHeader.Number);
                }
                else
                {
                    // Block expected to fail → verify it matches one of the expected exceptions
                    string expectedError = correctRlp[i].ExpectedException!;

                    // Map UNKNOWN_PARENT error
                    ErrorDetails actualDetails = EestErrorMapper.MapErrorToEEST("InvalidAncestor");

                    if (!MatchesExpectedException(actualDetails, expectedError))
                    {
                        return (parentHeader,
                            $"block #{correctRlp[i].Block.Number} failed with wrong exception. Expected: {expectedError}, Actual: {actualDetails.Code} (missing parent)",
                            parentHeader.Number);
                    }
                    // Expected failure occurred → skip to next block
                    continue;
                }
            }

            // Use found parent or genesis parent
            currentParentHeader ??= parentHeader;

            Assert.That(correctRlp[i].Block.Hash, Is.Not.Null, $"null hash in {test.Name} block {i}");

            // Validate block structure first (mimics SyncServer validation)
            if (blockValidator.ValidateSuggestedBlock(correctRlp[i].Block, currentParentHeader, out string? validationError))
            {
                try
                {
                    // All validations passed, suggest the block
                    blockTree.SuggestBlock(correctRlp[i].Block);
                    // Only update parent header if block was successfully added
                    parentHeader = correctRlp[i].Block.Header;
					if (expectsException)
					{
                        // Block succeeded but should have failed - report the expected exception
                        string expectedError = correctRlp[i].ExpectedException!;
                        ErrorDetails expectedDetails = EestErrorMapper.MapErrorToEEST(expectedError);
						return (parentHeader,
                            $"block (index {i}) insertion should have failed due to: {expectedDetails.Code}",
                            parentHeader.Number);
					}
                }
                catch (InvalidBlockException e)
                {
                    // Exception thrown during block processing
                    if (!expectsException)
                    {
                        return (parentHeader, $"block #" + correctRlp[i].Block.Number + " insertion into chain failed: " + e.Message, parentHeader.Number);
                    }
                    else
                    {
                        // Expected to fail - verify the exception matches one of the expected exceptions
                        string expectedError = correctRlp[i].ExpectedException!;
                        ErrorDetails actualDetails = EestErrorMapper.MapErrorToEEST(e.Message);

                        if (!MatchesExpectedException(actualDetails, expectedError))
                        {
                            return (parentHeader,
                                $"block #{correctRlp[i].Block.Number} failed with wrong exception. Expected: {expectedError}, Actual: {actualDetails.Code} (raw: {e.Message})",
                                parentHeader.Number);
                        }
                        // else: Expected exception matches actual exception → correct behavior
                    }
                }
                catch (Exception e)
                {
                    Assert.Fail($"Unexpected exception during processing: {e}");
                }
                finally
                {
                    // Dispose AccountChanges to prevent memory leaks in tests
                    correctRlp[i].Block.DisposeAccountChanges();
                }
            }
            else
            {
                // Validation FAILED
                if (!expectsException)
                {
                    return (parentHeader, $"block #" + correctRlp[i].Block.Number + " insertion into chain failed: " + validationError, parentHeader.Number);
                }
                else
                {
                    // Expected to fail - verify the exception matches one of the expected exceptions
                    string expectedError = correctRlp[i].ExpectedException!;
                    ErrorDetails actualDetails = EestErrorMapper.MapErrorToEEST(validationError!);

                    if (!MatchesExpectedException(actualDetails, expectedError))
                    {
                        return (parentHeader,
                            $"block #{correctRlp[i].Block.Number} failed with wrong exception. Expected: {expectedError}, Actual: {actualDetails.Code} (raw: {validationError})",
                            parentHeader.Number);
                    }
                    // else: Expected exception matches actual exception → correct behavior
                }
            }
        }
        return (parentHeader, null, parentHeader.Number);
    }

    /// <summary>
    /// Checks if an actual error matches any of the expected exceptions (pipe-separated format).
    /// Supports both single exceptions and pipe-separated lists: "BlockException.A|BlockException.B"
    /// Returns true if the actual error code matches ANY of the expected exceptions.
    /// </summary>
    private static bool MatchesExpectedException(ErrorDetails? actualDetails, string expectedError)
    {
        if (actualDetails is null || string.IsNullOrEmpty(expectedError))
            return false;

        // Handle pipe-separated exceptions: "BlockException.A|BlockException.B"
        // Test passes if actual error matches ANY of the expected exceptions
        string[] exceptions = expectedError.Split('|');
        foreach (string exc in exceptions)
        {
            string trimmedExc = exc.Trim();
            if (string.IsNullOrEmpty(trimmedExc))
                continue;

            // Try mapping the exception string to EEST format and comparing
            ErrorDetails expectedDetails = EestErrorMapper.MapErrorToEEST(trimmedExc);
            if (expectedDetails?.Code == actualDetails.Code)
            {
                return true;
            }
        }

        return false;
    }

    private async static Task RunNewPayloads(TestEngineNewPayloadsJson[]? newPayloads, IEngineRpcModule engineRpcModule)
    {
        (ExecutionPayloadV3, string[]?, string[]?, int, int)[] payloads = [.. JsonToEthereumTest.Convert(newPayloads)];

        // blockchain test engine
        foreach ((ExecutionPayload executionPayload, string[]? blobVersionedHashes, string[]? validationError, int newPayloadVersion, int fcuVersion) in payloads)
        {
            ResultWrapper<PayloadStatusV1> res;
            byte[]?[] hashes = blobVersionedHashes is null ? [] : [.. blobVersionedHashes.Select(x => Bytes.FromHexString(x))];

            MethodInfo newPayloadMethod = engineRpcModule.GetType().GetMethod($"engine_newPayloadV{newPayloadVersion}");
            List<object?> newPayloadParams = [executionPayload];
            if (newPayloadVersion >= 3)
            {
                newPayloadParams.AddRange([hashes, executionPayload.ParentBeaconBlockRoot]);
            }
            if (newPayloadVersion >= 4)
            {
                newPayloadParams.Add(executionPayload.ExecutionRequests);
            }

            res = await (Task<ResultWrapper<PayloadStatusV1>>)newPayloadMethod.Invoke(engineRpcModule, [.. newPayloadParams]);

            if (res.Result.ResultType == ResultType.Success)
            {
                ForkchoiceStateV1 fcuState = new(executionPayload.BlockHash, executionPayload.BlockHash, executionPayload.BlockHash);
                MethodInfo fcuMethod = engineRpcModule.GetType().GetMethod($"engine_forkchoiceUpdatedV{fcuVersion}");
                await (Task<ResultWrapper<ForkchoiceUpdatedV1Result>>)fcuMethod.Invoke(engineRpcModule, [fcuState, null]);
            }
        }
    }

    private static List<(Block Block, string ExpectedException)> DecodeRlps(BlockchainTest test, bool failOnInvalidRlp)
    {
        List<(Block Block, string ExpectedException)> correctRlp = [];
        for (int i = 0; i < test.Blocks!.Length; i++)
        {
            TestBlockJson testBlockJson = test.Blocks[i];
            try
            {
                RlpStream rlpContext = Bytes.FromHexString(testBlockJson.Rlp!).AsRlpStream();
                Block suggestedBlock = Rlp.Decode<Block>(rlpContext);

                // Always add decoded blocks to correctRlp, even if BlockHeader is null
                // This mirrors geth's behavior: decode and insert all blocks, then check validation results
                if (testBlockJson.BlockHeader is not null)
                {
                    // Verify header hash matches test JSON expectation
                    Assert.That(suggestedBlock.Header.Hash, Is.EqualTo(new Hash256(testBlockJson.BlockHeader.Hash)));

                    for (int uncleIndex = 0; uncleIndex < suggestedBlock.Uncles.Length; uncleIndex++)
                    {
                        Assert.That(suggestedBlock.Uncles[uncleIndex].Hash, Is.EqualTo(new Hash256(testBlockJson.UncleHeaders![uncleIndex].Hash)));
                    }
                }

                // Add block regardless of BlockHeader presence
                correctRlp.Add((suggestedBlock, testBlockJson.ExpectedException));
            }
            catch (Exception e)
            {
                if (testBlockJson.ExpectedException is null)
                {
                    string invalidRlpMessage = $"Invalid RLP ({i}) {e}";
                    Assert.That(!failOnInvalidRlp, invalidRlpMessage);
                    // ForgedTests don't have ExpectedException and at the same time have invalid rlps
                    // Don't fail here. If test executed incorrectly will fail at last check
                    _logger.Warn(invalidRlpMessage);
                }
                else
                {
                    _logger.Info($"Expected invalid RLP ({i})");
                }
            }
        }

        if (correctRlp.Count == 0)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(test.GenesisBlockHeader, Is.Not.Null);
                Assert.That(test.LastBlockHash, Is.EqualTo(new Hash256(test.GenesisBlockHeader.Hash)));
            }
        }

        return correctRlp;
    }

    private static void InitializeTestState(BlockchainTest test, IWorldState stateProvider, ISpecProvider specProvider)
    {
        foreach (KeyValuePair<Address, AccountState> accountState in
            (IEnumerable<KeyValuePair<Address, AccountState>>)test.Pre ?? Array.Empty<KeyValuePair<Address, AccountState>>())
        {
            foreach (KeyValuePair<UInt256, byte[]> storageItem in accountState.Value.Storage)
            {
                stateProvider.Set(new StorageCell(accountState.Key, storageItem.Key), storageItem.Value);
            }

            stateProvider.CreateAccount(accountState.Key, accountState.Value.Balance, accountState.Value.Nonce);
            stateProvider.InsertCode(accountState.Key, accountState.Value.Code, specProvider.GenesisSpec);
        }

        stateProvider.Commit(specProvider.GenesisSpec);
        stateProvider.CommitTree(0);
        stateProvider.Reset();
    }

    private static List<string> RunAssertions(BlockchainTest test, Block headBlock, IWorldState stateProvider)
    {
        if (test.PostStateRoot is not null)
        {
            return test.PostStateRoot != stateProvider.StateRoot ? ["state root mismatch"] : Enumerable.Empty<string>().ToList();
        }

        List<string> differences = [];

        IEnumerable<KeyValuePair<Address, AccountState>> deletedAccounts = test.Pre?
            .Where(pre => !(test.PostState?.ContainsKey(pre.Key) ?? false)) ?? Array.Empty<KeyValuePair<Address, AccountState>>();

        foreach (KeyValuePair<Address, AccountState> deletedAccount in deletedAccounts)
        {
            if (stateProvider.AccountExists(deletedAccount.Key))
            {
                differences.Add($"Pre state account {deletedAccount.Key} was not deleted as expected.");
            }
        }

        foreach ((Address accountAddress, AccountState accountState) in test.PostState!)
        {
            int differencesBefore = differences.Count;

            if (differences.Count > 8)
            {
                Console.WriteLine("More than 8 differences...");
                break;
            }

            bool accountExists = stateProvider.AccountExists(accountAddress);
            UInt256? balance = accountExists ? stateProvider.GetBalance(accountAddress) : null;
            UInt256? nonce = accountExists ? stateProvider.GetNonce(accountAddress) : null;

            if (accountState.Balance != balance)
            {
                differences.Add($"{accountAddress} balance exp: {accountState.Balance}, actual: {balance}, diff: {(balance > accountState.Balance ? balance - accountState.Balance : accountState.Balance - balance)}");
            }

            if (accountState.Nonce != nonce)
            {
                differences.Add($"{accountAddress} nonce exp: {accountState.Nonce}, actual: {nonce}");
            }

            byte[] code = accountExists ? stateProvider.GetCode(accountAddress) : [];
            if (!Bytes.AreEqual(accountState.Code, code))
            {
                differences.Add($"{accountAddress} code exp: {accountState.Code?.Length}, actual: {code?.Length}");
            }

            if (differences.Count != differencesBefore)
            {
                _logger.Info($"ACCOUNT STATE ({accountAddress}) HAS DIFFERENCES");
            }

            differencesBefore = differences.Count;

            KeyValuePair<UInt256, byte[]>[] clearedStorages = [];
            if (test.Pre.ContainsKey(accountAddress))
            {
                clearedStorages = [.. test.Pre[accountAddress].Storage.Where(s => !accountState.Storage.ContainsKey(s.Key))];
            }

            foreach (KeyValuePair<UInt256, byte[]> clearedStorage in clearedStorages)
            {
                ReadOnlySpan<byte> value = !stateProvider.AccountExists(accountAddress) ? Bytes.Empty : stateProvider.Get(new StorageCell(accountAddress, clearedStorage.Key));
                if (!value.IsZero())
                {
                    differences.Add($"{accountAddress} storage[{clearedStorage.Key}] exp: 0x00, actual: {value.ToHexString(true)}");
                }
            }

            foreach (KeyValuePair<UInt256, byte[]> storageItem in accountState.Storage)
            {
                ReadOnlySpan<byte> value = !stateProvider.AccountExists(accountAddress) ? Bytes.Empty : stateProvider.Get(new StorageCell(accountAddress, storageItem.Key));
                if (!Bytes.AreEqual(storageItem.Value, value))
                {
                    differences.Add($"{accountAddress} storage[{storageItem.Key}] exp: {storageItem.Value.ToHexString(true)}, actual: {value.ToHexString(true)}");
                }
            }

            if (differences.Count != differencesBefore)
            {
                _logger.Info($"ACCOUNT STORAGE ({accountAddress}) HAS DIFFERENCES");
            }
        }

        TestBlockHeaderJson? testHeaderJson = test.Blocks?
                                                 .Where(b => b.BlockHeader is not null)
                                                 .SingleOrDefault(b => new Hash256(b.BlockHeader.Hash) == headBlock.Hash)?.BlockHeader;

        if (testHeaderJson is not null)
        {
            BlockHeader testHeader = JsonToEthereumTest.Convert(testHeaderJson);

            BigInteger gasUsed = headBlock.Header.GasUsed;
            if ((testHeader?.GasUsed ?? 0) != gasUsed)
            {
                differences.Add($"GAS USED exp: {testHeader?.GasUsed ?? 0}, actual: {gasUsed}");
            }

            if (headBlock.Transactions.Length != 0 && testHeader.Bloom.ToString() != headBlock.Header.Bloom.ToString())
            {
                differences.Add($"BLOOM exp: {testHeader.Bloom}, actual: {headBlock.Header.Bloom}");
            }

            if (testHeader.StateRoot != stateProvider.StateRoot)
            {
                differences.Add($"STATE ROOT exp: {testHeader.StateRoot}, actual: {stateProvider.StateRoot}");
            }

            if (testHeader.TxRoot != headBlock.Header.TxRoot)
            {
                differences.Add($"TRANSACTIONS ROOT exp: {testHeader.TxRoot}, actual: {headBlock.Header.TxRoot}");
            }

            if (testHeader.ReceiptsRoot != headBlock.Header.ReceiptsRoot)
            {
                differences.Add($"RECEIPT ROOT exp: {testHeader.ReceiptsRoot}, actual: {headBlock.Header.ReceiptsRoot}");
            }
        }

        if (test.LastBlockHash != headBlock.Hash)
        {
            differences.Add($"LAST BLOCK HASH exp: {test.LastBlockHash}, actual: {headBlock.Hash}");
        }

        foreach (string difference in differences)
        {
            _logger.Info(difference);
        }

        return differences;
    }
}
