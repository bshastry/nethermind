// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

namespace Nethermind.Test.Runner.Validator;

/// <summary>
/// Configuration options for corpus validation.
/// </summary>
public sealed class ValidatorOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether to dump divergent traces for debugging.
    /// </summary>
    public bool DumpTraces { get; set; }

    /// <summary>
    /// Gets or sets the output directory for dumps and reports.
    /// </summary>
    public string? OutputDir { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to enable divergence clustering analysis.
    /// </summary>
    public bool Triage { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to output JSON report.
    /// </summary>
    public bool JsonOutput { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to suppress progress output.
    /// </summary>
    public bool Quiet { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to include filtered entries in trace dumps.
    /// </summary>
    public bool IncludeFilteredInDump { get; set; }

    public ValidatorOptions()
    {
        DumpTraces = false;
        OutputDir = null;
        Triage = false;
        JsonOutput = false;
        Quiet = false;
        IncludeFilteredInDump = false;
    }
}
