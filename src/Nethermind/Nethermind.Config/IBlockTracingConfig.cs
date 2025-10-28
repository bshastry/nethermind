// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

namespace Nethermind.Config;

/// <summary>
/// Configuration for EIP block-level execution tracing.
/// </summary>
public interface IBlockTracingConfig : IConfig
{
    /// <summary>
    /// Enable block-level tracing (default: false).
    /// </summary>
    [ConfigItem(DefaultValue = "false", Description = "Enable block-level execution tracing for consensus bug detection.")]
    bool Enabled { get; set; }

    /// <summary>
    /// Trace level: minimal, standard, or full (default: full).
    /// </summary>
    [ConfigItem(DefaultValue = "full", Description = "Trace level: 'minimal' (blockStart/End, txStart/End), 'standard' (+ pre/post execution, validation), 'full' (+ operations, trie operations).")]
    string TraceLevel { get; set; }

    /// <summary>
    /// Output file path for traces (default: null = disabled).
    /// </summary>
    [ConfigItem(Description = "Output file path for block-level traces in JSON Lines format. If not set, tracing is disabled.")]
    string OutputFile { get; set; }

    /// <summary>
    /// Buffer size for trace output in lines (default: 1000).
    /// </summary>
    [ConfigItem(DefaultValue = "1000", Description = "Number of trace records to buffer before flushing to disk.")]
    int BufferSize { get; set; }
}
