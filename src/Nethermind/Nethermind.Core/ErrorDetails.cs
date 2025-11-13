// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Collections.Generic;

namespace Nethermind.Core;

/// <summary>
/// Error details for canonical EEST (Ethereum Execution Spec Test) format.
/// Provides structured, machine-parsable error information for test failures.
/// </summary>
public class ErrorDetails
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, object>? Context { get; set; }
}
