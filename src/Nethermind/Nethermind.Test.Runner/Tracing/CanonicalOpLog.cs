// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Collections.Generic;
using Nethermind.Int256;

namespace Nethermind.Test.Runner.Tracing;

/// <summary>
/// Represents a normalized EVM execution step for cross-client comparison.
/// This format strips client-specific quirks to enable deterministic trace hashing.
/// Matches goevmlab's CustomMarshal format with ClearGascost=true, ClearMemSize=true,
/// ClearReturndata=true (the defaults used for cross-client comparison).
/// </summary>
public sealed class CanonicalOpLog
{
    /// <summary>
    /// Call depth (1-based, 0 means not a real opcode)
    /// </summary>
    public int Depth { get; set; }

    /// <summary>
    /// Program counter
    /// </summary>
    public long Pc { get; set; }

    /// <summary>
    /// EOF section number (EOF only, 0 if not EOF)
    /// </summary>
    public long Section { get; set; }

    /// <summary>
    /// EOF function depth (EOF only, 0 if not EOF)
    /// </summary>
    public int FunctionDepth { get; set; }

    /// <summary>
    /// Gas remaining before this operation
    /// </summary>
    public long Gas { get; set; }

    /// <summary>
    /// Opcode byte (0x00-0xFF)
    /// </summary>
    public int Op { get; set; }

    /// <summary>
    /// Opcode name (matches geth's vm.OpCode.String() output)
    /// </summary>
    public string OpName { get; set; } = string.Empty;

    /// <summary>
    /// Stack contents (last 6 items only for deterministic comparison).
    /// Items are stored in the order they appear on the stack.
    /// </summary>
    public List<UInt256> Stack { get; set; } = [];
}
