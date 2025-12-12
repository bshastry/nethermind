// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Nethermind.Test.Runner.Validator;

/// <summary>
/// High-performance parallel corpus validator for cross-VM consensus verification.
/// </summary>
/// <remarks>
/// This is the main entry point for validating a cross-client enhanced corpus against Nethermind's execution.
/// It loads all .json files from the corpus directory, spawns worker tasks, validates each file's trace hash
/// against Nethermind's execution, and produces a detailed report of matches and divergences.
/// <para>
/// Example usage:
/// <code>
/// var validator = new CorpusValidator(
///     corpusPath: "/path/to/corpus",
///     fork: "Prague",
///     workerCount: 16,
///     options: new ValidatorOptions { DumpTraces = true }
/// );
///
/// var report = await validator.ValidateAsync(CancellationToken.None);
/// report.PrintConsoleSummary();
/// </code>
/// </para>
/// </remarks>
public sealed class CorpusValidator
{
    private const int ProgressIntervalMs = 3000;
    private const int ChannelCapacity = 10000;

    private readonly string _corpusPath;
    private readonly string _fork;
    private readonly int _workerCount;
    private readonly ValidatorOptions _options;

    /// <summary>
    /// Initializes a new instance of the CorpusValidator.
    /// </summary>
    /// <param name="corpusPath">Directory or file path containing corpus entries</param>
    /// <param name="fork">EVM fork to use (e.g., "Osaka", "Prague", "Cancun")</param>
    /// <param name="workerCount">Number of parallel workers (0 = auto-detect)</param>
    /// <param name="options">Configuration options</param>
    public CorpusValidator(
        string corpusPath,
        string fork,
        int workerCount = 0,
        ValidatorOptions? options = null)
    {
        _corpusPath = corpusPath ?? throw new ArgumentNullException(nameof(corpusPath));
        _fork = fork ?? throw new ArgumentNullException(nameof(fork));
        _workerCount = workerCount > 0 ? workerCount : Environment.ProcessorCount;
        _options = options ?? new ValidatorOptions();
    }

    /// <summary>
    /// Occurs when validation progress is updated.
    /// </summary>
    public event EventHandler<ProgressEventArgs>? Progress;

    private void OnProgress(string message)
    {
        Progress?.Invoke(this, new ProgressEventArgs(message));
    }

    /// <summary>
    /// Validates all corpus entries and returns the report.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation report with results</returns>
    /// <exception cref="IOException">Thrown if corpus cannot be loaded</exception>
    public async Task<ValidationReport> ValidateAsync(CancellationToken cancellationToken = default)
    {
        // Load corpus files
        List<string> corpusFiles = LoadCorpusFiles();

        if (corpusFiles.Count == 0)
        {
            throw new IOException($"No .json files found in corpus path: {_corpusPath}");
        }

        if (!_options.Quiet)
        {
            Console.WriteLine("=== Cross-VM Corpus Validator ===");
            Console.WriteLine();
            Console.WriteLine($"Corpus:  {_corpusPath}");
            Console.WriteLine($"Files:   {corpusFiles.Count}");
            Console.WriteLine($"Fork:    {_fork}");
            Console.WriteLine($"Workers: {_workerCount}");
            if (_options.DumpTraces && _options.OutputDir is not null)
            {
                Console.WriteLine($"Dumps:   {_options.OutputDir}");
            }
            Console.WriteLine();
        }

        // Create report
        ValidationReport report = new(_corpusPath, _fork, _workerCount);

        // Create trace dump directory if needed
        string? traceDumpDir = null;
        if (_options.DumpTraces && _options.OutputDir is not null)
        {
            traceDumpDir = Path.Combine(_options.OutputDir, "traces");
            Directory.CreateDirectory(traceDumpDir);
        }

        // Create job channel
        Channel<string> jobChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait
        });

        // Start workers
        List<Task> workerTasks = new(_workerCount);
        for (int i = 0; i < _workerCount; i++)
        {
            ValidationWorker worker = new(
                i,
                jobChannel.Reader,
                report,
                _fork,
                traceDumpDir,
                _options.IncludeFilteredInDump);

            workerTasks.Add(worker.RunAsync(cancellationToken));
        }

        // Start progress reporter with its own cancellation source
        using CancellationTokenSource progressCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task? progressTask = null;
        if (!_options.Quiet)
        {
            progressTask = Task.Run(async () =>
            {
                using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(ProgressIntervalMs));

                while (!progressCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await timer.WaitForNextTickAsync(progressCts.Token);
                        string progressStr = report.GetProgressString();
                        Console.WriteLine(progressStr);
                        OnProgress(progressStr);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }, progressCts.Token);
        }

        // Enqueue all jobs
        try
        {
            foreach (string filePath in corpusFiles)
            {
                await jobChannel.Writer.WriteAsync(filePath, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation requested
        }
        finally
        {
            jobChannel.Writer.Complete();
        }

        // Wait for all workers to complete
        await Task.WhenAll(workerTasks);

        // Cancel progress reporter now that workers are done
        if (progressTask is not null)
        {
            progressCts.Cancel();
            try
            {
                await progressTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        // Print final report
        if (!_options.Quiet)
        {
            report.PrintConsoleSummary();
        }

        // Write JSON report if requested
        if (_options.JsonOutput && _options.OutputDir is not null)
        {
            string jsonPath = Path.Combine(_options.OutputDir, "validation-report.json");
            report.WriteJsonReport(jsonPath);

            if (!_options.Quiet)
            {
                Console.WriteLine();
                Console.WriteLine($"JSON report written to: {jsonPath}");
            }
        }

        return report;
    }

    /// <summary>
    /// Loads all .json files from the corpus path.
    /// </summary>
    /// <returns>List of file paths</returns>
    /// <exception cref="IOException">Thrown if path does not exist</exception>
    private List<string> LoadCorpusFiles()
    {
        List<string> files = new();

        if (!File.Exists(_corpusPath) && !Directory.Exists(_corpusPath))
        {
            throw new IOException($"Corpus path does not exist: {_corpusPath}");
        }

        if (File.Exists(_corpusPath))
        {
            // Single file mode
            if (_corpusPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                files.Add(_corpusPath);
            }
            return files;
        }

        // Directory mode - recursively find all .json files
        files.AddRange(Directory.EnumerateFiles(
            _corpusPath,
            "*.json",
            SearchOption.AllDirectories));

        return files;
    }
}

/// <summary>
/// Progress event arguments.
/// </summary>
public sealed class ProgressEventArgs : EventArgs
{
    /// <summary>
    /// Gets the progress message.
    /// </summary>
    public string Message { get; }

    public ProgressEventArgs(string message)
    {
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }
}
