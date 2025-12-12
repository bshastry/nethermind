// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Security.Cryptography;
using System.Text;
using Nethermind.Int256;

namespace Nethermind.Test.Runner.Tracing;

/// <summary>
/// Normalizes EVM traces to a canonical format for cross-VM comparison.
/// Processes trace lines, filters out noise, and produces a deterministic
/// MD5 hash that matches geth's normalizer exactly.
/// </summary>
/// <remarks>
/// Key normalization rules:
/// <list type="bullet">
/// <item>Filter depth=0 entries (not real opcodes)</item>
/// <item>Filter STOP opcodes (op=0x00)</item>
/// <item>Skip duplicate lines with same (pc, depth, functionDepth)</item>
/// <item>Stack: last 6 items only, lowercase hex, minimal representation (no leading zeros)</item>
/// <item>Include stateRoot as final line before hash finalization</item>
/// </list>
/// </remarks>
public sealed class TraceNormalizer
{
    private readonly MD5 _md5 = MD5.Create();
    private int _lineCount;
    private CanonicalOpLog? _prev; // For duplicate line detection

    /// <summary>
    /// Gets the number of trace lines processed (included in hash)
    /// </summary>
    public int LineCount => _lineCount;

    /// <summary>
    /// Process a single trace log entry. Applies filtering and writes normalized output to MD5 hasher.
    /// </summary>
    /// <param name="log">The canonical op log entry</param>
    public void ProcessLog(CanonicalOpLog log)
    {
        // Filter 1: depth=0 means not a real opcode
        if (log.Depth == 0)
        {
            return;
        }

        // Filter 2: STOP opcodes (geth continues on virtual STOP at end of code)
        if (log.Op == 0x00)
        {
            return;
        }

        // Filter 3: Skip duplicate lines with same (pc, depth, functionDepth)
        // Geth sometimes outputs two lines for the same opcode when there's an error
        if (_prev is not null &&
            _prev.Pc == log.Pc &&
            _prev.Depth == log.Depth &&
            _prev.FunctionDepth == log.FunctionDepth)
        {
            return; // Skip duplicate
        }

        // Write previous line (if any) and store current as new prev
        if (_prev is not null)
        {
            WriteNormalized(_prev);
        }
        _prev = log;
    }

    /// <summary>
    /// Finish processing and return the MD5 hash bytes.
    /// This flushes any pending log entry and adds the stateRoot as the final line.
    /// </summary>
    /// <param name="stateRoot">The post-execution state root (0x-prefixed hex)</param>
    /// <returns>The MD5 hash bytes (16 bytes)</returns>
    public byte[] Finish(string stateRoot)
    {
        // Flush pending log entry
        if (_prev is not null)
        {
            WriteNormalized(_prev);
            _prev = null;
        }

        // Write stateRoot as final line: {"stateRoot":"0x..."}
        string rootLine = $"{{\"stateRoot\":\"{stateRoot}\"}}";
        byte[] rootBytes = Encoding.UTF8.GetBytes(rootLine);
        _md5.TransformBlock(rootBytes, 0, rootBytes.Length, null, 0);
        _md5.TransformBlock([0x0A], 0, 1, null, 0); // newline
        _lineCount++;

        // Finalize hash
        _md5.TransformFinalBlock([], 0, 0);
        return _md5.Hash ?? throw new InvalidOperationException("MD5 hash is null");
    }

    /// <summary>
    /// Finish processing and return a TracingResult with hash and metadata.
    /// </summary>
    /// <param name="stateRoot">The post-execution state root (0x-prefixed hex)</param>
    /// <returns>A TracingResult containing the hash, state root, and line count</returns>
    public TracingResult FinishWithResult(string stateRoot)
    {
        byte[] hash = Finish(stateRoot);

        return new TracingResult
        {
            TraceHash = BytesToHex(hash),
            StateRoot = stateRoot,
            TraceLines = _lineCount
        };
    }

    /// <summary>
    /// Reset the normalizer for reuse
    /// </summary>
    public void Reset()
    {
        _md5.Initialize();
        _lineCount = 0;
        _prev = null;
    }

    /// <summary>
    /// Writes a normalized log entry to the MD5 hasher
    /// </summary>
    private void WriteNormalized(CanonicalOpLog log)
    {
        byte[] data = CanonicalMarshal(log);
        _md5.TransformBlock(data, 0, data.Length, null, 0);
        _md5.TransformBlock([0x0A], 0, 1, null, 0); // newline
        _lineCount++;
    }

    /// <summary>
    /// Produces deterministic JSON output for an oplog.
    /// This mirrors goevmlab's CustomMarshal with default settings.
    /// Field order is fixed for deterministic hashing: depth, pc, [section], [functionDepth], gas, op, opName, stack
    /// </summary>
    /// <param name="log">The log entry</param>
    /// <returns>The canonical JSON bytes (UTF-8)</returns>
    private static byte[] CanonicalMarshal(CanonicalOpLog log)
    {
        var sb = new StringBuilder(256);

        // Fixed field order for deterministic output (matches goevmlab/geth)
        sb.Append("{\"depth\":");
        sb.Append(log.Depth);

        sb.Append(",\"pc\":");
        sb.Append(log.Pc);

        // EOF-specific fields (omit if zero)
        if (log.Section != 0)
        {
            sb.Append(",\"section\":");
            sb.Append(log.Section);
        }

        if (log.FunctionDepth != 0)
        {
            sb.Append(",\"functionDepth\":");
            sb.Append(log.FunctionDepth);
        }

        sb.Append(",\"gas\":");
        sb.Append(log.Gas);

        // Op as hex with 0x prefix (2 digits, zero-padded)
        sb.Append(",\"op\":\"0x");
        sb.Append((log.Op & 0xFF).ToString("x2"));
        sb.Append('"');

        // OpName - derive from opcode to match geth's vm.OpCode(op).String()
        sb.Append(",\"opName\":\"");
        sb.Append(log.OpName);
        sb.Append('"');

        // Stack: last 6 items only for deterministic comparison
        sb.Append(",\"stack\":[");
        if (log.Stack.Count > 0)
        {
            int start = Math.Max(0, log.Stack.Count - 6);
            for (int i = start; i < log.Stack.Count; i++)
            {
                if (i != start)
                {
                    sb.Append(',');
                }
                sb.Append('"');
                sb.Append(FormatStackItem(log.Stack[i]));
                sb.Append('"');
            }
        }
        sb.Append(']');

        sb.Append('}');
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Formats a stack item as lowercase hex with minimal representation (no leading zeros).
    /// Must match geth's uint256.Int.Hex() output exactly.
    /// </summary>
    /// <remarks>
    /// Examples: 0x0, 0xf, 0xff, 0xdeadbeef (not 0x00, 0x0f, 0x00ff)
    /// </remarks>
    /// <param name="item">The stack item</param>
    /// <returns>The formatted hex string</returns>
    private static string FormatStackItem(in UInt256 item)
    {
        if (item.IsZero)
        {
            return "0x0";
        }

        // Convert to hex string and remove leading zeros
        Span<byte> bytes = stackalloc byte[32];
        item.ToBigEndian(bytes);

        // Find first non-zero byte
        int start = 0;
        while (start < bytes.Length && bytes[start] == 0)
        {
            start++;
        }

        if (start == bytes.Length)
        {
            return "0x0"; // All zeros (shouldn't happen if item.IsZero check worked)
        }

        // Build hex string without leading zeros
        var sb = new StringBuilder(2 + (bytes.Length - start) * 2);
        sb.Append("0x");

        // First byte: don't pad with leading zero if < 0x10
        byte firstByte = bytes[start];
        if (firstByte < 0x10)
        {
            sb.Append(firstByte.ToString("x")); // Single digit, no leading zero
        }
        else
        {
            sb.Append(firstByte.ToString("x2")); // Two digits
        }

        // Subsequent bytes: always 2 digits
        for (int i = start + 1; i < bytes.Length; i++)
        {
            sb.Append(bytes[i].ToString("x2"));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Converts bytes to lowercase hex string
    /// </summary>
    private static string BytesToHex(byte[] bytes)
    {
        var hex = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes)
        {
            hex.Append(b.ToString("x2"));
        }
        return hex.ToString();
    }
}
