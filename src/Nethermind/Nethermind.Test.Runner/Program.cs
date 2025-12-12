// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.CommandLine;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ethereum.Test.Base;
using Ethereum.Test.Base.Interfaces;
using Nethermind.Specs;
using Nethermind.Test.Runner.Validator;

namespace Nethermind.Test.Runner;

internal class Program
{
    public class Options
    {
        public static Option<string> Input { get; } =
            new("--input", "-i") { Description = "Set the state test input file or directory. Either 'input' or 'stdin' is required." };

        public static Option<string> Filter { get; } =
            new("--filter", "-f") { Description = "Set the test name that you want to run. Could also be a regular expression." };

        public static Option<bool> BlockTest { get; } =
            new("--blockTest", "-b") { Description = "Set test as blockTest. if not, it will be by default assumed a state test." };

        public static Option<bool> EofTest { get; } =
            new("--eofTest", "-e") { Description = "Set test as eofTest. if not, it will be by default assumed a state test." };
        public static Option<bool> TraceAlways { get; } =
            new("--trace", "-t") { Description = "Set to always trace (by default traces are only generated for failing tests)." };

        public static Option<bool> TraceNever { get; } =
            new("--neverTrace", "-n") { Description = "Set to never trace (by default traces are only generated for failing tests). [Only for State Test]" };

        public static Option<bool> ExcludeMemory { get; } =
            new("--memory", "-m") { Description = "Exclude memory trace." };

        public static Option<bool> ExcludeStack { get; } =
            new("--stack", "-s") { Description = "Exclude stack trace." };

        public static Option<bool> Wait { get; } =
            new("--wait", "-w") { Description = "Wait for input after the test run." };

        public static Option<bool> Stdin { get; } =
            new("--stdin", "-x") { Description = "If stdin is used, the state runner will read inputs (filenames) from stdin, and continue executing until empty line is read." };

        public static Option<bool> GnosisTest { get; } =
            new("--gnosisTest", "-g") { Description = "Set test as gnosisTest. if not, it will be by default assumed a mainnet test." };

        public static Option<bool> EnableWarmup { get; } =
            new("--warmup", "-wu") { Description = "Enable warmup for benchmarking purposes." };

        public static Option<bool> ValidateCorpus { get; } =
            new("--validateCorpus", "-v") { Description = "Run cross-VM corpus validation mode." };

        public static Option<string> CorpusDir { get; } =
            new("--corpusDir", "-c") { Description = "Directory containing corpus files to validate." };

        public static Option<string> Fork { get; } =
            new("--fork") { Description = "EVM fork name (e.g., Prague, Osaka, Cancun). Default: Prague" };

        public static Option<int> Workers { get; } =
            new("--workers") { Description = "Number of worker threads. Default: CPU count" };

        public static Option<bool> DumpTraces { get; } =
            new("--dumpTraces") { Description = "Dump traces for divergent tests." };

        public static Option<string> OutputDir { get; } =
            new("--outputDir", "-o") { Description = "Output directory for reports and trace dumps." };

        public static Option<bool> JsonReport { get; } =
            new("--json") { Description = "Output JSON report." };

        public static Option<bool> Quiet { get; } =
            new("--quiet", "-q") { Description = "Suppress progress output." };

        public static Option<bool> Triage { get; } =
            new("--triage") { Description = "Enable divergence triage/clustering." };
    }

    public static async Task<int> Main(params string[] args)
    {
        RootCommand rootCommand =
        [
            Options.Input,
            Options.Filter,
            Options.BlockTest,
            Options.EofTest,
            Options.TraceAlways,
            Options.TraceNever,
            Options.ExcludeMemory,
            Options.ExcludeStack,
            Options.Wait,
            Options.Stdin,
            Options.GnosisTest,
            Options.EnableWarmup,
            Options.ValidateCorpus,
            Options.CorpusDir,
            Options.Fork,
            Options.Workers,
            Options.DumpTraces,
            Options.OutputDir,
            Options.JsonReport,
            Options.Quiet,
            Options.Triage,
        ];
        rootCommand.SetAction(Run);

        return await rootCommand.Parse(args).InvokeAsync();
    }

    private static async Task<int> Run(ParseResult parseResult, CancellationToken cancellationToken)
    {
        if (parseResult.GetValue(Options.ValidateCorpus))
        {
            return await RunCorpusValidation(parseResult, cancellationToken);
        }

        WhenTrace whenTrace = WhenTrace.WhenFailing;

        if (parseResult.GetValue(Options.TraceNever))
            whenTrace = WhenTrace.Never;

        if (parseResult.GetValue(Options.TraceAlways))
            whenTrace = WhenTrace.Always;

        string input = parseResult.GetValue(Options.Input);

        if (parseResult.GetValue(Options.Stdin))
            input = Console.ReadLine();
        ulong chainId = parseResult.GetValue(Options.GnosisTest) ? GnosisSpecProvider.Instance.ChainId : MainnetSpecProvider.Instance.ChainId;


        while (!string.IsNullOrWhiteSpace(input))
        {
            if (parseResult.GetValue(Options.BlockTest))
            {
                await RunBlockTest(input, source => new BlockchainTestsRunner(
                    source,
                    parseResult.GetValue(Options.Filter),
                    chainId,
                    parseResult.GetValue(Options.TraceAlways),
                    !parseResult.GetValue(Options.ExcludeMemory),
                    parseResult.GetValue(Options.ExcludeStack)));
            }
            else if (parseResult.GetValue(Options.EofTest))
            {
                RunEofTest(input, source => new EofTestsRunner(
                    source,
                    parseResult.GetValue(Options.Filter)));
            }
            else
            {
                RunStateTest(input, source => new StateTestsRunner(
                    source,
                    whenTrace,
                    !parseResult.GetValue(Options.ExcludeMemory),
                    !parseResult.GetValue(Options.ExcludeStack),
                    chainId,
                    parseResult.GetValue(Options.Filter),
                    parseResult.GetValue(Options.EnableWarmup)));
            }


            if (!parseResult.GetValue(Options.Stdin))
                break;

            input = Console.ReadLine();
        }

        if (parseResult.GetValue(Options.Wait))
            Console.ReadLine();

        return 0;
    }

    private static async Task RunBlockTest(string path, Func<ITestSourceLoader, IBlockchainTestRunner> testRunnerBuilder)
    {
        ITestSourceLoader source = Path.HasExtension(path)
            ? new TestsSourceLoader(new LoadBlockchainTestFileStrategy(), path)
            : new TestsSourceLoader(new LoadBlockchainTestsStrategy(), path);
        await testRunnerBuilder(source).RunTestsAsync();
    }

    private static void RunEofTest(string path, Func<ITestSourceLoader, IEofTestRunner> testRunnerBuilder)
    {
        ITestSourceLoader source = Path.HasExtension(path)
            ? new TestsSourceLoader(new LoadEofTestFileStrategy(), path)
            : new TestsSourceLoader(new LoadEofTestsStrategy(), path);
        testRunnerBuilder(source).RunTests();
    }

    private static void RunStateTest(string path, Func<ITestSourceLoader, IStateTestRunner> testRunnerBuilder)
    {
        ITestSourceLoader source = Path.HasExtension(path)
            ? new TestsSourceLoader(new LoadGeneralStateTestFileStrategy(), path)
            : new TestsSourceLoader(new LoadGeneralStateTestsStrategy(), path);
        testRunnerBuilder(source).RunTests();
    }

    private static async Task<int> RunCorpusValidation(ParseResult parseResult, CancellationToken cancellationToken)
    {
        string corpusDir = parseResult.GetValue(Options.CorpusDir);
        string fork = parseResult.GetValue(Options.Fork) ?? "Prague";
        int workers = parseResult.GetValue(Options.Workers);
        if (workers <= 0) workers = Environment.ProcessorCount;

        var options = new ValidatorOptions
        {
            DumpTraces = parseResult.GetValue(Options.DumpTraces),
            OutputDir = parseResult.GetValue(Options.OutputDir),
            JsonOutput = parseResult.GetValue(Options.JsonReport),
            Quiet = parseResult.GetValue(Options.Quiet),
            Triage = parseResult.GetValue(Options.Triage)
        };

        var validator = new CorpusValidator(corpusDir, fork, workers, options);
        var report = await validator.ValidateAsync(cancellationToken);

        // Exit codes: 0=all pass, 1=divergences, 2=errors
        if (report.HasErrors) return 2;
        if (report.HasDivergences) return 1;
        return 0;
    }
}
