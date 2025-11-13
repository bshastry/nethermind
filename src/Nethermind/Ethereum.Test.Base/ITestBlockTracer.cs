// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Specs;
using Nethermind.Evm.Tracing;

namespace Ethereum.Test.Base;

public interface ITestBlockTracer : IBlockTracer
{
    /// <summary>
    /// Reports test completion with optional error details.
    /// </summary>
    /// <param name="testName">Name of the test</param>
    /// <param name="pass">Whether the test passed</param>
    /// <param name="spec">Fork specification</param>
    /// <param name="duration">Test execution duration</param>
    /// <param name="headStateRoot">State root of the head block (lastValidStateRoot if test failed)</param>
    /// <param name="error">DEPRECATED: Legacy error message, no longer written to output. Use errorDetails instead.</param>
    /// <param name="errorDetails">Structured error details following EEST canonical format</param>
    /// <param name="lastValidBlock">Index of the last successfully validated block (for failed tests)</param>
    void TestFinished(
        string testName,
        bool pass,
        IReleaseSpec spec,
        TimeSpan? duration,
        Hash256? headStateRoot,
        string? error = null,
        ErrorDetails? errorDetails = null,
        long? lastValidBlock = null);
}
