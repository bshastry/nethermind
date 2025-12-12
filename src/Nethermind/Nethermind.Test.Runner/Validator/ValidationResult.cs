// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;

namespace Nethermind.Test.Runner.Validator;

/// <summary>
/// Result of validating a single corpus entry against Nethermind's execution.
/// </summary>
public sealed class ValidationResult
{
    /// <summary>
    /// Possible outcomes of validation.
    /// </summary>
    public enum ValidationStatus
    {
        /// <summary>
        /// Trace hashes matched - consensus agreement.
        /// </summary>
        Passed,

        /// <summary>
        /// Trace hashes differed - consensus divergence detected.
        /// </summary>
        Diverged,

        /// <summary>
        /// Test was skipped (no metadata, invalid format, etc.).
        /// </summary>
        Skipped,

        /// <summary>
        /// Execution error occurred.
        /// </summary>
        Error
    }

    /// <summary>
    /// Gets the file path of the corpus entry.
    /// </summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the test name (from EEST format).
    /// </summary>
    public string? TestName { get; init; }

    /// <summary>
    /// Gets the validation status.
    /// </summary>
    public ValidationStatus Status { get; init; }

    /// <summary>
    /// Gets the reason for skipping (if status is Skipped).
    /// </summary>
    public string? SkipReason { get; init; }

    /// <summary>
    /// Gets the client that generated the expected trace.
    /// </summary>
    public string? SourceClient { get; init; }

    /// <summary>
    /// Gets the version of the source client.
    /// </summary>
    public string? SourceVersion { get; init; }

    /// <summary>
    /// Gets the expected trace hash.
    /// </summary>
    public string? ExpectedTraceHash { get; init; }

    /// <summary>
    /// Gets the expected state root.
    /// </summary>
    public string? ExpectedStateRoot { get; init; }

    /// <summary>
    /// Gets the expected number of trace lines.
    /// </summary>
    public int? ExpectedTraceLines { get; init; }

    /// <summary>
    /// Gets the actual trace hash from Nethermind execution.
    /// </summary>
    public string? ActualTraceHash { get; init; }

    /// <summary>
    /// Gets the actual state root from Nethermind execution.
    /// </summary>
    public string? ActualStateRoot { get; init; }

    /// <summary>
    /// Gets the actual number of trace lines from Nethermind execution.
    /// </summary>
    public int? ActualTraceLines { get; init; }

    /// <summary>
    /// Gets the execution time.
    /// </summary>
    public TimeSpan? ExecutionTime { get; init; }

    /// <summary>
    /// Gets the error message (if status is Error).
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Checks if the result represents a passed test.
    /// </summary>
    public bool IsPassed => Status == ValidationStatus.Passed;

    /// <summary>
    /// Checks if the result represents a divergence.
    /// </summary>
    public bool IsDiverged => Status == ValidationStatus.Diverged;

    /// <summary>
    /// Checks if the result represents a skipped test.
    /// </summary>
    public bool IsSkipped => Status == ValidationStatus.Skipped;

    /// <summary>
    /// Checks if the result represents an error.
    /// </summary>
    public bool IsError => Status == ValidationStatus.Error;

    /// <summary>
    /// Checks if state roots match (when both are available).
    /// </summary>
    public bool StateRootMatches =>
        ExpectedStateRoot is null || ActualStateRoot is null ||
        ExpectedStateRoot.Equals(ActualStateRoot, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a PASSED result.
    /// </summary>
    public static ValidationResult Passed(
        string filePath,
        string? testName,
        string sourceClient,
        string? sourceVersion,
        string expectedHash,
        string? expectedStateRoot,
        int? expectedTraceLines,
        string actualHash,
        string? actualStateRoot,
        int? actualTraceLines,
        TimeSpan executionTime) => new()
        {
            FilePath = filePath,
            TestName = testName,
            Status = ValidationStatus.Passed,
            SourceClient = sourceClient,
            SourceVersion = sourceVersion,
            ExpectedTraceHash = expectedHash,
            ExpectedStateRoot = expectedStateRoot,
            ExpectedTraceLines = expectedTraceLines,
            ActualTraceHash = actualHash,
            ActualStateRoot = actualStateRoot,
            ActualTraceLines = actualTraceLines,
            ExecutionTime = executionTime
        };

    /// <summary>
    /// Creates a DIVERGED result.
    /// </summary>
    public static ValidationResult Diverged(
        string filePath,
        string? testName,
        string sourceClient,
        string? sourceVersion,
        string expectedHash,
        string? expectedStateRoot,
        int? expectedTraceLines,
        string actualHash,
        string? actualStateRoot,
        int? actualTraceLines,
        TimeSpan executionTime) => new()
        {
            FilePath = filePath,
            TestName = testName,
            Status = ValidationStatus.Diverged,
            SourceClient = sourceClient,
            SourceVersion = sourceVersion,
            ExpectedTraceHash = expectedHash,
            ExpectedStateRoot = expectedStateRoot,
            ExpectedTraceLines = expectedTraceLines,
            ActualTraceHash = actualHash,
            ActualStateRoot = actualStateRoot,
            ActualTraceLines = actualTraceLines,
            ExecutionTime = executionTime
        };

    /// <summary>
    /// Creates a SKIPPED result.
    /// </summary>
    public static ValidationResult Skipped(string filePath, string reason) => new()
    {
        FilePath = filePath,
        Status = ValidationStatus.Skipped,
        SkipReason = reason
    };

    /// <summary>
    /// Creates an ERROR result.
    /// </summary>
    public static ValidationResult Error(string filePath, string errorMessage) => new()
    {
        FilePath = filePath,
        Status = ValidationStatus.Error,
        ErrorMessage = errorMessage
    };

    public override string ToString()
    {
        return Status switch
        {
            ValidationStatus.Passed => $"PASSED: {FilePath} ({FormatDuration(ExecutionTime)})",
            ValidationStatus.Diverged => $"DIVERGED: {FilePath}\n  expected: {ExpectedTraceHash}\n  actual:   {ActualTraceHash}",
            ValidationStatus.Skipped => $"SKIPPED: {FilePath} ({SkipReason})",
            ValidationStatus.Error => $"ERROR: {FilePath} ({ErrorMessage})",
            _ => $"UNKNOWN: {FilePath}"
        };
    }

    private static string FormatDuration(TimeSpan? duration)
    {
        if (duration is null) return "?";

        double ms = duration.Value.TotalMilliseconds;
        return ms < 1000 ? $"{ms:F0}ms" : $"{ms / 1000.0:F2}s";
    }
}
