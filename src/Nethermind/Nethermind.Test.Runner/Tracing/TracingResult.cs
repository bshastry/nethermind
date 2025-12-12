// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

namespace Nethermind.Test.Runner.Tracing;

/// <summary>
/// Contains the result of trace normalization, including the MD5 hash and metadata.
/// </summary>
public sealed class TracingResult
{
    /// <summary>
    /// MD5 hash of the normalized trace (32 hex characters, lowercase)
    /// </summary>
    public string TraceHash { get; set; } = string.Empty;

    /// <summary>
    /// Post-execution state root (0x-prefixed hex string)
    /// </summary>
    public string StateRoot { get; set; } = string.Empty;

    /// <summary>
    /// Number of trace lines that were included in the hash
    /// </summary>
    public int TraceLines { get; set; }
}
