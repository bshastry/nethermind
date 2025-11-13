// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Ethereum.Test.Base;
using Ethereum.Test.Base.Interfaces;
using Nethermind.Blockchain.Tracing;
using Nethermind.Blockchain.Tracing.BlockLevel;
using Nethermind.Blockchain.Tracing.GethStyle;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Specs;
using Nethermind.Evm.Tracing;
using Nethermind.Evm.Tracing.BlockOperations;
using Nethermind.Int256;

namespace Nethermind.Test.Runner;

public class BlockchainTestsRunner(
    ITestSourceLoader testsSource,
    string? filter,
    ulong chainId,
    bool trace = false,
    bool traceMemory = false,
    bool traceNoStack = false,
    string? txTraceFile = null,
    string? blockTraceFile = null,
    string? blockTraceLevel = null)
    : BlockchainTestBase, IBlockchainTestRunner
{
    private readonly ConsoleColor _defaultColor = Console.ForegroundColor;
    private readonly ITestSourceLoader _testsSource = testsSource ?? throw new ArgumentNullException(nameof(testsSource));

    public async Task<IEnumerable<EthereumTestResult>> RunTestsAsync()
    {
        List<EthereumTestResult> testResults = new();
        IEnumerable<BlockchainTest> tests = _testsSource.LoadTests<BlockchainTest>();

        // Create transaction tracer if requested
        BlockchainTestStreamingTracer? txTracer = null;
        StreamWriter? txTraceWriter = null;

        // Backwards compatibility: --trace flag maps to --tx-trace stderr
        string? effectiveTxTraceFile = txTraceFile ?? (trace ? "stderr" : null);

        if (!string.IsNullOrEmpty(effectiveTxTraceFile))
        {
            try
            {
                Stream txOutputStream;

                if (effectiveTxTraceFile.Equals("stderr", StringComparison.OrdinalIgnoreCase) ||
                    effectiveTxTraceFile.Equals("-", StringComparison.OrdinalIgnoreCase))
                {
                    txOutputStream = Console.OpenStandardError();
                    Console.WriteLine("Transaction tracing enabled: stderr");
                }
                else
                {
                    txTraceWriter = new StreamWriter(effectiveTxTraceFile, append: false);
                    txOutputStream = txTraceWriter.BaseStream;
                    Console.WriteLine($"Transaction tracing enabled: {effectiveTxTraceFile}");
                }

                txTracer = new BlockchainTestStreamingTracer(
                    new() { EnableMemory = traceMemory, DisableStack = traceNoStack },
                    txOutputStream);
            }
            catch (Exception ex)
            {
                WriteRed($"Failed to create transaction trace output: {ex.Message}");
            }
        }

        // Prepare block-level tracing writer if requested
        TextWriter? blockTraceWriter = null;

        // If block-trace-level is set but block-trace is not, default to stderr
        string? effectiveBlockTraceFile = blockTraceFile;
        if (string.IsNullOrEmpty(effectiveBlockTraceFile) && !string.IsNullOrEmpty(blockTraceLevel))
        {
            effectiveBlockTraceFile = "stderr";
        }

        if (!string.IsNullOrEmpty(effectiveBlockTraceFile))
        {
            try
            {
                // Determine output: file or stderr
                if (effectiveBlockTraceFile.Equals("stderr", StringComparison.OrdinalIgnoreCase) ||
                    effectiveBlockTraceFile.Equals("-", StringComparison.OrdinalIgnoreCase))
                {
                    // Write to stderr
                    blockTraceWriter = Console.Error;
                }
                else
                {
                    // Write to file
                    blockTraceWriter = new StreamWriter(effectiveBlockTraceFile, append: false);
                }

                TraceLevel level = ParseTraceLevel(blockTraceLevel);

                if (blockTraceWriter == Console.Error)
                    Console.WriteLine($"Block-level tracing enabled: stderr (level: {level})");
                else
                    Console.WriteLine($"Block-level tracing enabled: {effectiveBlockTraceFile} (level: {level})");
            }
            catch (Exception ex)
            {
                WriteRed($"Failed to create block trace output: {ex.Message}");
            }
        }

        try
        {
            foreach (BlockchainTest test in tests)
            {
                if (filter is not null && test.Name is not null && !Regex.Match(test.Name, $"^({filter})").Success)
                    continue;
                Setup();

                Console.Write($"{test,-120} ");
                if (test.LoadFailure is not null)
                {
                    WriteRed(test.LoadFailure);
                    testResults.Add(new EthereumTestResult(test.Name, test.LoadFailure));
                }
                else
                {
                    test.ChainId = chainId;

                    // Create block-level tracer for this test with fork name
                    BlockLevelJsonTracer? blockTracer = null;
                    if (blockTraceWriter is not null)
                    {
                        try
                        {
                            // Extract fork name from test network spec, preserving capitalization (e.g., "Osaka", "Prague")
                            string? forkName = test.Network?.Name;

                            TraceLevel level = ParseTraceLevel(blockTraceLevel);
                            blockTracer = new BlockLevelJsonTracer(blockTraceWriter, level, ownsWriter: false, forkName: forkName);
                        }
                        catch (Exception ex)
                        {
                            WriteRed($"Failed to create block tracer for test: {ex.Message}");
                        }
                    }

                    // Combine tracers if needed
                    ITestBlockTracer? effectiveTracer = null;

                    if (txTracer is not null && blockTracer is not null)
                    {
                        effectiveTracer = new CompositeTestBlockTracer(txTracer, blockTracer);
                    }
                    else if (txTracer is not null)
                    {
                        effectiveTracer = txTracer;
                    }
                    else if (blockTracer is not null)
                    {
                        effectiveTracer = new BlockLevelTestTracerAdapter(blockTracer);
                    }

                    EthereumTestResult result = await RunTest(test, tracer: effectiveTracer);
                    testResults.Add(result);
                    if (result.Pass)
                        WriteGreen("PASS");
                    else
                        WriteRed("FAIL");

                    // Clean up per-test block tracer
                    blockTracer?.Dispose();
                }
            }
        }
        finally
        {
            // Clean up tracer and writer resources
            txTracer?.Dispose();
            txTraceWriter?.Dispose();
            // Block tracer is disposed per-test, just clean up writer if owned
            if (blockTraceWriter is not null && blockTraceWriter != Console.Error)
            {
                blockTraceWriter?.Dispose();
            }
        }

        return testResults;
    }

    /// <summary>
    /// Parses the trace level string to TraceLevel enum.
    /// </summary>
    private static TraceLevel ParseTraceLevel(string? level)
    {
        return (level?.ToLower()) switch
        {
            "minimal" => TraceLevel.Minimal,
            "standard" => TraceLevel.Standard,
            "full" => TraceLevel.Full,
            null => TraceLevel.Full,
            _ => TraceLevel.Full
        };
    }

    private void WriteRed(string text)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(text);
        Console.ForegroundColor = _defaultColor;
    }

    private void WriteGreen(string text)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(text);
        Console.ForegroundColor = _defaultColor;
    }

    /// <summary>
    /// Adapter to wrap a BlockLevelJsonTracer as an ITestBlockTracer.
    /// Uses BlockReceiptsTracer to collect receipt data and pass it to the block-level tracer.
    /// </summary>
    private class BlockLevelTestTracerAdapter : ITestBlockTracer
    {
        private readonly BlockReceiptsTracer _receiptsTracer;
        private readonly BlockLevelJsonTracer _blockTracer;

        public BlockLevelTestTracerAdapter(BlockLevelJsonTracer inner)
        {
            ArgumentNullException.ThrowIfNull(inner);
            _blockTracer = inner;
            _receiptsTracer = new BlockReceiptsTracer();
            _receiptsTracer.SetOtherTracer(inner);
        }

        public bool IsTracingRewards => _receiptsTracer.IsTracingRewards;
        public void ReportReward(Address author, string rewardType, UInt256 rewardValue)
            => _receiptsTracer.ReportReward(author, rewardType, rewardValue);
        public void StartNewBlockTrace(Block block) => _receiptsTracer.StartNewBlockTrace(block);
        public ITxTracer StartNewTxTrace(Transaction? tx) => _receiptsTracer.StartNewTxTrace(tx);
        public void EndTxTrace() => _receiptsTracer.EndTxTrace();
        public void EndTxTrace(TxReceipt? receipt) => _receiptsTracer.EndTxTrace(receipt);
        public void EndBlockTrace()
        {
            _receiptsTracer.EndBlockTrace();
            // Finalize block trace after EndBlockTrace completes to write blockEnd with state root and bloom
            _blockTracer.FinalizeBlockTrace();
        }
        public void TracePreExecution(PreExecutionOperation operation) => _receiptsTracer.TracePreExecution(operation);
        public void TracePostExecution(PostExecutionOperation operation) => _receiptsTracer.TracePostExecution(operation);
        public void TraceValidation(ValidationOperation operation) => _receiptsTracer.TraceValidation(operation);
        public void TraceTrieOperation(TrieOperation operation) => _receiptsTracer.TraceTrieOperation(operation);

        public void TestFinished(string testName, bool pass, IReleaseSpec spec, TimeSpan? duration, Hash256? headStateRoot, string? error = null, ErrorDetails? errorDetails = null, long? lastValidBlock = null)
        {
            _blockTracer.WriteTestEndMarker(testName, pass, spec, duration, headStateRoot, error, errorDetails, lastValidBlock);
        }
    }

    /// <summary>
    /// Composite tracer that delegates to both a test tracer and a block-level tracer.
    /// Uses BlockReceiptsTracer to collect receipt data and pass it to the block-level tracer.
    /// </summary>
    private class CompositeTestBlockTracer : ITestBlockTracer
    {
        private readonly ITestBlockTracer _testTracer;
        private readonly BlockReceiptsTracer _receiptsTracer;
        private readonly BlockLevelJsonTracer _blockTracer;

        public CompositeTestBlockTracer(ITestBlockTracer testTracer, BlockLevelJsonTracer blockTracer)
        {
            _testTracer = testTracer ?? throw new ArgumentNullException(nameof(testTracer));
            ArgumentNullException.ThrowIfNull(blockTracer);

            _blockTracer = blockTracer;

            // Wrap block tracer in BlockReceiptsTracer to collect and pass receipts
            _receiptsTracer = new BlockReceiptsTracer();
            _receiptsTracer.SetOtherTracer(blockTracer);
        }

        public bool IsTracingRewards => _testTracer.IsTracingRewards || _receiptsTracer.IsTracingRewards;

        public void ReportReward(Address author, string rewardType, UInt256 rewardValue)
        {
            if (_testTracer.IsTracingRewards)
                _testTracer.ReportReward(author, rewardType, rewardValue);
            if (_receiptsTracer.IsTracingRewards)
                _receiptsTracer.ReportReward(author, rewardType, rewardValue);
        }

        public void StartNewBlockTrace(Block block)
        {
            _testTracer.StartNewBlockTrace(block);
            _receiptsTracer.StartNewBlockTrace(block);
        }

        public ITxTracer StartNewTxTrace(Transaction? tx)
        {
            ITxTracer testTxTracer = _testTracer.StartNewTxTrace(tx);
            ITxTracer blockTxTracer = _receiptsTracer.StartNewTxTrace(tx);

            // Return the test tracer's tx tracer (block tracer typically returns NullTxTracer)
            return testTxTracer;
        }

        public void EndTxTrace()
        {
            _testTracer.EndTxTrace();
            _receiptsTracer.EndTxTrace();
        }

        public void EndTxTrace(TxReceipt? receipt)
        {
            _testTracer.EndTxTrace();
            _receiptsTracer.EndTxTrace(receipt);
        }

        public void EndBlockTrace()
        {
            _testTracer.EndBlockTrace();
            _receiptsTracer.EndBlockTrace();
            // Finalize block trace after EndBlockTrace completes to write blockEnd with state root and bloom
            _blockTracer.FinalizeBlockTrace();
        }

        public void TracePreExecution(PreExecutionOperation operation)
        {
            _receiptsTracer.TracePreExecution(operation);
        }

        public void TracePostExecution(PostExecutionOperation operation)
        {
            _receiptsTracer.TracePostExecution(operation);
        }

        public void TraceValidation(ValidationOperation operation)
        {
            _receiptsTracer.TraceValidation(operation);
        }

        public void TraceTrieOperation(TrieOperation operation)
        {
            _receiptsTracer.TraceTrieOperation(operation);
        }

        public void TestFinished(string testName, bool pass, IReleaseSpec spec, TimeSpan? duration, Hash256? headStateRoot, string? error = null, ErrorDetails? errorDetails = null, long? lastValidBlock = null)
        {
            _testTracer.TestFinished(testName, pass, spec, duration, headStateRoot, error, errorDetails, lastValidBlock);
            _blockTracer.WriteTestEndMarker(testName, pass, spec, duration, headStateRoot, error, errorDetails, lastValidBlock);
        }
    }
}
