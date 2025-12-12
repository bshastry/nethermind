// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Nethermind.Test.Runner.Validator;

/// <summary>
/// Aggregates validation results and produces reports.
/// Thread-safe for concurrent updates from multiple workers.
/// </summary>
public sealed class ValidationReport
{
    private const string ReportVersion = "1.0";

    private readonly string _corpusPath;
    private readonly string _fork;
    private readonly int _workers;
    private readonly DateTime _startTime;

    private int _totalTests;
    private int _passedTests;
    private int _divergedTests;
    private int _skippedTests;
    private int _errorTests;
    private long _totalExecutionMs;

    private readonly List<ValidationResult> _divergences = new();
    private readonly List<ValidationResult> _errors = new();
    private readonly object _lock = new();

    public ValidationReport(string corpusPath, string fork, int workers)
    {
        _corpusPath = corpusPath ?? throw new ArgumentNullException(nameof(corpusPath));
        _fork = fork ?? throw new ArgumentNullException(nameof(fork));
        _workers = workers;
        _startTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Records a validation result (thread-safe).
    /// </summary>
    public void RecordResult(ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        Interlocked.Increment(ref _totalTests);

        if (result.ExecutionTime.HasValue)
        {
            Interlocked.Add(ref _totalExecutionMs, (long)result.ExecutionTime.Value.TotalMilliseconds);
        }

        switch (result.Status)
        {
            case ValidationResult.ValidationStatus.Passed:
                Interlocked.Increment(ref _passedTests);
                break;

            case ValidationResult.ValidationStatus.Diverged:
                Interlocked.Increment(ref _divergedTests);
                lock (_lock)
                {
                    _divergences.Add(result);
                }
                break;

            case ValidationResult.ValidationStatus.Skipped:
                Interlocked.Increment(ref _skippedTests);
                break;

            case ValidationResult.ValidationStatus.Error:
                Interlocked.Increment(ref _errorTests);
                lock (_lock)
                {
                    _errors.Add(result);
                }
                break;
        }
    }

    public int TotalTests => _totalTests;
    public int PassedTests => _passedTests;
    public int DivergedTests => _divergedTests;
    public int SkippedTests => _skippedTests;
    public int ErrorTests => _errorTests;

    public IReadOnlyList<ValidationResult> Divergences
    {
        get
        {
            lock (_lock)
            {
                return _divergences.ToList();
            }
        }
    }

    public IReadOnlyList<ValidationResult> Errors
    {
        get
        {
            lock (_lock)
            {
                return _errors.ToList();
            }
        }
    }

    public TimeSpan ElapsedTime => DateTime.UtcNow - _startTime;

    public double PassRate
    {
        get
        {
            int validated = _passedTests + _divergedTests;
            return validated == 0 ? 100.0 : 100.0 * _passedTests / validated;
        }
    }

    public TimeSpan AverageExecutionTime
    {
        get
        {
            int executed = _passedTests + _divergedTests + _errorTests;
            if (executed == 0) return TimeSpan.Zero;
            return TimeSpan.FromMilliseconds(_totalExecutionMs / (double)executed);
        }
    }

    public bool HasDivergences => _divergedTests > 0;
    public bool HasErrors => _errorTests > 0;

    /// <summary>
    /// Gets a progress string for periodic updates.
    /// </summary>
    public string GetProgressString()
    {
        TimeSpan elapsed = ElapsedTime;
        double rate = _totalTests / (elapsed.TotalSeconds + 0.001);

        return $"[{FormatDuration(elapsed)}] processed: {_totalTests} ({rate:F1}/s) | " +
               $"passed: {_passedTests} | diverged: {_divergedTests} | skipped: {_skippedTests} | errors: {_errorTests}";
    }

    /// <summary>
    /// Prints console summary report.
    /// </summary>
    public void PrintConsoleSummary()
    {
        TimeSpan elapsed = ElapsedTime;

        Console.WriteLine();
        Console.WriteLine("========================================");
        Console.WriteLine("Cross-VM Corpus Validation Complete");
        Console.WriteLine("========================================");
        Console.WriteLine();
        Console.WriteLine($"Corpus:     {_corpusPath}");
        Console.WriteLine($"Fork:       {_fork}");
        Console.WriteLine($"Workers:    {_workers}");
        Console.WriteLine($"Duration:   {FormatDuration(elapsed)}");
        Console.WriteLine();
        Console.WriteLine("Results:");
        Console.WriteLine($"  Total:    {_totalTests}");
        Console.WriteLine($"  Passed:   {_passedTests}");
        Console.WriteLine($"  Diverged: {_divergedTests}");
        Console.WriteLine($"  Skipped:  {_skippedTests}");
        Console.WriteLine($"  Errors:   {_errorTests}");
        Console.WriteLine();
        Console.WriteLine($"Pass Rate:  {PassRate:F1}%");
        Console.WriteLine($"Avg Time:   {FormatDuration(AverageExecutionTime)}");
        Console.WriteLine("========================================");

        if (HasDivergences)
        {
            Console.WriteLine();
            Console.WriteLine("CONSENSUS DIVERGENCES DETECTED!");
            Console.WriteLine();
            Console.WriteLine("Diverged tests:");

            List<ValidationResult> divs = Divergences.ToList();
            foreach (ValidationResult div in divs)
            {
                Console.WriteLine($"  {GetFileName(div.FilePath)}");
                Console.WriteLine($"    expected: {div.ExpectedTraceHash}");
                Console.WriteLine($"    actual:   {div.ActualTraceHash}");
                if (!div.StateRootMatches)
                {
                    Console.WriteLine("    stateRoot mismatch!");
                }
            }
        }

        if (HasErrors)
        {
            Console.WriteLine();
            Console.WriteLine("Errors:");

            List<ValidationResult> errs = Errors.ToList();
            foreach (ValidationResult err in errs)
            {
                Console.WriteLine($"  {GetFileName(err.FilePath)}: {err.ErrorMessage}");
            }
        }
    }

    /// <summary>
    /// Writes JSON report to file.
    /// </summary>
    public void WriteJsonReport(string outputPath)
    {
        string json = ToJsonString();
        File.WriteAllText(outputPath, json);
    }

    /// <summary>
    /// Returns the JSON report as a string.
    /// </summary>
    public string ToJsonString()
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();

        // Metadata
        writer.WriteString("version", ReportVersion);
        writer.WriteString("timestamp", DateTime.UtcNow.ToString("O"));
        writer.WriteString("nethermindVersion", GetNethermindVersion());

        // Configuration
        writer.WritePropertyName("config");
        writer.WriteStartObject();
        writer.WriteString("corpusPath", _corpusPath);
        writer.WriteString("fork", _fork);
        writer.WriteNumber("workers", _workers);
        writer.WriteEndObject();

        // Results summary
        writer.WritePropertyName("results");
        writer.WriteStartObject();
        writer.WriteNumber("total", _totalTests);
        writer.WriteNumber("passed", _passedTests);
        writer.WriteNumber("diverged", _divergedTests);
        writer.WriteNumber("skipped", _skippedTests);
        writer.WriteNumber("errors", _errorTests);
        writer.WriteNumber("passRate", PassRate);
        writer.WriteNumber("durationMs", (long)ElapsedTime.TotalMilliseconds);
        writer.WriteNumber("avgExecutionMs", (long)AverageExecutionTime.TotalMilliseconds);
        writer.WriteEndObject();

        // Divergences
        if (HasDivergences)
        {
            writer.WritePropertyName("divergences");
            writer.WriteStartArray();

            List<ValidationResult> divs = Divergences.ToList();
            foreach (ValidationResult div in divs)
            {
                writer.WriteStartObject();
                writer.WriteString("path", div.FilePath);
                if (div.TestName is not null)
                {
                    writer.WriteString("testName", div.TestName);
                }
                if (div.SourceClient is not null)
                {
                    writer.WriteString("sourceClient", div.SourceClient);
                }
                if (div.ExpectedTraceHash is not null)
                {
                    writer.WriteString("expectedTraceHash", div.ExpectedTraceHash);
                }
                if (div.ActualTraceHash is not null)
                {
                    writer.WriteString("actualTraceHash", div.ActualTraceHash);
                }
                if (div.ExpectedStateRoot is not null)
                {
                    writer.WriteString("expectedStateRoot", div.ExpectedStateRoot);
                }
                if (div.ActualStateRoot is not null)
                {
                    writer.WriteString("actualStateRoot", div.ActualStateRoot);
                }
                writer.WriteBoolean("stateRootMatch", div.StateRootMatches);
                if (div.ExpectedTraceLines.HasValue)
                {
                    writer.WriteNumber("expectedTraceLines", div.ExpectedTraceLines.Value);
                }
                if (div.ActualTraceLines.HasValue)
                {
                    writer.WriteNumber("actualTraceLines", div.ActualTraceLines.Value);
                }
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        // Errors
        if (HasErrors)
        {
            writer.WritePropertyName("errors");
            writer.WriteStartArray();

            List<ValidationResult> errs = Errors.ToList();
            foreach (ValidationResult err in errs)
            {
                writer.WriteStartObject();
                writer.WriteString("path", err.FilePath);
                if (err.ErrorMessage is not null)
                {
                    writer.WriteString("error", err.ErrorMessage);
                }
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string FormatDuration(TimeSpan duration)
    {
        long totalSeconds = (long)duration.TotalSeconds;
        long hours = totalSeconds / 3600;
        long minutes = (totalSeconds % 3600) / 60;
        long seconds = totalSeconds % 60;
        long millis = duration.Milliseconds;

        if (hours > 0)
        {
            return $"{hours:D2}:{minutes:D2}:{seconds:D2}";
        }
        else if (minutes > 0)
        {
            return $"{minutes:D2}:{seconds:D2}";
        }
        else if (seconds > 0)
        {
            return $"{seconds}.{millis:D3}s";
        }
        else
        {
            return $"{millis}ms";
        }
    }

    private static string GetFileName(string path)
    {
        if (path is null) return "unknown";

        int lastSlash = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        return lastSlash >= 0 && lastSlash < path.Length - 1
            ? path[(lastSlash + 1)..]
            : path;
    }

    private static string GetNethermindVersion()
    {
        var assembly = typeof(ValidationReport).Assembly;
        var version = assembly.GetName().Version;
        return version?.ToString() ?? "dev";
    }
}
