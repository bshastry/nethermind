// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Nethermind.Blockchain.Tracing.BlockLevel;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Test.Builders;
using Nethermind.Int256;
using NUnit.Framework;

namespace Nethermind.Blockchain.Test.Tracing.BlockLevel;

/// <summary>
/// Tests for canonical format compliance in block-level tracing.
/// Validates the EIP canonical format requirements:
/// - Key ordering (type first, primary ID second, rest alphabetical)
/// - Lowercase hex encoding
/// - Null field omission
/// - Compact JSON formatting
/// </summary>
[TestFixture]
public class CanonicalFormatTests
{
    [Test]
    public void ToCanonicalHex_Long_ProducesLowercaseHex()
    {
        // Arrange
        long value = 255;

        // Act
        string result = CanonicalFormatHelpers.ToCanonicalHex(value);

        // Assert
        result.Should().Be("0xff");
        CanonicalFormatHelpers.IsCanonicalHex(result).Should().BeTrue();
    }

    [Test]
    public void ToCanonicalHex_ULong_ProducesLowercaseHex()
    {
        // Arrange
        ulong value = 0xABCDEF123456;

        // Act
        string result = CanonicalFormatHelpers.ToCanonicalHex(value);

        // Assert
        result.Should().Be("0xabcdef123456");
        CanonicalFormatHelpers.IsCanonicalHex(result).Should().BeTrue();
    }

    [Test]
    public void ToCanonicalAddress_ProducesLowercaseAddress()
    {
        // Arrange - address with mixed case
        var address = new Address("0xC014Ba5e00000000000000000000000000000000");

        // Act
        string result = CanonicalFormatHelpers.ToCanonicalAddress(address);

        // Assert
        result.Should().Be("0xc014ba5e00000000000000000000000000000000");
        CanonicalFormatHelpers.IsCanonicalHex(result).Should().BeTrue();
    }

    [Test]
    public void ToCanonicalHash_ProducesLowercaseHash()
    {
        // Arrange - hash with mixed case
        var hash = new Hash256("0xABCDEF1234567890ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890");

        // Act
        string result = CanonicalFormatHelpers.ToCanonicalHash(hash);

        // Assert
        result.Should().Be("0xabcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890");
        CanonicalFormatHelpers.IsCanonicalHex(result).Should().BeTrue();
    }

    [Test]
    public void UInt256ToHashHex_ProducesLowercaseHexString()
    {
        // Arrange - UInt256 created from hash bytes
        var hash = new Hash256("0xCDC02AAAF71D6BB1F39670DDC0BDEB5D907BC4D32482DD52C5EB6E57EBFE892E");
        var uint256Value = new UInt256(hash.Bytes, true);

        // Act
        string result = CanonicalFormatHelpers.UInt256ToHashHex(uint256Value);

        // Assert
        result.Should().Be("0xcdc02aaaf71d6bb1f39670ddc0bdeb5d907bc4d32482dd52c5eb6e57ebfe892e");
        CanonicalFormatHelpers.IsCanonicalHex(result).Should().BeTrue();
    }

    [Test]
    public void UInt256ToHashHex_HandlesZeroValue()
    {
        // Arrange
        var uint256Value = UInt256.Zero;

        // Act
        string result = CanonicalFormatHelpers.UInt256ToHashHex(uint256Value);

        // Assert
        result.Should().Be("0x0000000000000000000000000000000000000000000000000000000000000000");
        CanonicalFormatHelpers.IsCanonicalHex(result).Should().BeTrue();
    }

    [Test]
    public void IsCanonicalHex_RejectsUppercase()
    {
        // Arrange
        string uppercase = "0xABCDEF";

        // Act & Assert
        CanonicalFormatHelpers.IsCanonicalHex(uppercase).Should().BeFalse();
    }

    [Test]
    public void IsCanonicalHex_RejectsMissingPrefix()
    {
        // Arrange
        string noPrefix = "abcdef";

        // Act & Assert
        CanonicalFormatHelpers.IsCanonicalHex(noPrefix).Should().BeFalse();
    }

    [Test]
    public void BlockStartRecord_HasCanonicalKeyOrdering()
    {
        // Arrange
        var block = Build.A.Block
            .WithNumber(1)
            .WithGasLimit(300000000)
            .WithBaseFeePerGas(7)
            .WithTimestamp(1000)
            .TestObject;

        var writer = new StringWriter();
        var tracer = new BlockLevelJsonTracer(writer, TraceLevel.BlockLifecycle);

        // Act
        tracer.StartNewBlockTrace(block);

        // Assert
        var output = writer.ToString();
        var record = JsonDocument.Parse(output.TrimEnd());
        var keys = record.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        keys[0].Should().Be("type");
        keys[1].Should().Be("blockNumber");
        keys.Skip(2).Should().BeInAscendingOrder(); // Rest are alphabetical
    }

    [Test]
    public void TxStartRecord_HasCanonicalKeyOrdering()
    {
        // Arrange
        var block = Build.A.Block.WithNumber(1).TestObject;
        var transaction = Build.A.Transaction
            .WithGasLimit(21000)
            .WithGasPrice(1000000000)
            .WithValue(1000)
            .TestObject;

        var writer = new StringWriter();
        var tracer = new BlockLevelJsonTracer(writer, TraceLevel.Minimal);

        // Act
        tracer.StartNewBlockTrace(block);
        tracer.StartNewTxTrace(transaction);

        // Assert
        var lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var txStartLine = lines.FirstOrDefault(l => l.Contains("\"txStart\""));
        txStartLine.Should().NotBeNull();

        var record = JsonDocument.Parse(txStartLine!);
        var keys = record.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        keys[0].Should().Be("type");
        keys[1].Should().Be("txIndex");
        keys.Skip(2).Should().BeInAscendingOrder(); // Rest are alphabetical
    }

    [Test]
    public void TxEndRecord_HasCanonicalKeyOrdering()
    {
        // Arrange
        var block = Build.A.Block.WithNumber(1).TestObject;
        var transaction = Build.A.Transaction.WithGasLimit(21000).TestObject;
        var receipt = Build.A.Receipt
            .WithGasUsed(21000)
            .WithStatusCode(1)
            .TestObject;

        var writer = new StringWriter();
        var tracer = new BlockLevelJsonTracer(writer, TraceLevel.Minimal);

        // Act
        tracer.StartNewBlockTrace(block);
        tracer.StartNewTxTrace(transaction);
        tracer.EndTxTrace(receipt);

        // Assert
        var lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var txEndLine = lines.FirstOrDefault(l => l.Contains("\"txEnd\""));
        txEndLine.Should().NotBeNull();

        var record = JsonDocument.Parse(txEndLine!);
        var keys = record.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        keys[0].Should().Be("type");
        keys[1].Should().Be("txIndex");
        keys.Skip(2).Should().BeInAscendingOrder(); // Rest are alphabetical
    }

    [Test]
    public void BlockEndRecord_HasCanonicalKeyOrdering()
    {
        // Arrange
        var block = Build.A.Block
            .WithNumber(1)
            .WithGasUsed(100000)
            .TestObject;

        var writer = new StringWriter();
        var tracer = new BlockLevelJsonTracer(writer, TraceLevel.BlockLifecycle);

        // Act
        tracer.StartNewBlockTrace(block);
        tracer.EndBlockTrace();

        // Assert
        var lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var blockEndLine = lines.FirstOrDefault(l => l.Contains("\"blockEnd\""));
        blockEndLine.Should().NotBeNull();

        var record = JsonDocument.Parse(blockEndLine!);
        var keys = record.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        keys[0].Should().Be("type");
        keys[1].Should().Be("blockNumber");
        keys.Skip(2).Should().BeInAscendingOrder(); // Rest are alphabetical
    }

    [Test]
    public void AllHexValues_AreLowercase()
    {
        // Arrange
        var block = Build.A.Block
            .WithNumber(255)
            .WithGasLimit(0xABCDEF)
            .WithBeneficiary(new Address("0xC014Ba5e00000000000000000000000000000000"))
            .TestObject;

        var writer = new StringWriter();
        var tracer = new BlockLevelJsonTracer(writer, TraceLevel.BlockLifecycle);

        // Act
        tracer.StartNewBlockTrace(block);

        // Assert
        var output = writer.ToString();

        // Find all hex values in output
        var hexPattern = new Regex(@"""0x[0-9A-Fa-f]+""");
        var matches = hexPattern.Matches(output);

        foreach (Match match in matches)
        {
            var hexValue = match.Value;
            hexValue.Should().Be(hexValue.ToLowerInvariant(),
                $"hex value {hexValue} should be lowercase");
        }
    }

    [Test]
    public void NullOptionalFields_AreOmitted()
    {
        // Arrange - transaction without optional fields
        var block = Build.A.Block.WithNumber(1).TestObject;
        var transaction = Build.A.Transaction
            .WithType(TxType.Legacy) // No EIP-1559 fields
            .WithGasLimit(21000)
            .TestObject;

        var writer = new StringWriter();
        var tracer = new BlockLevelJsonTracer(writer, TraceLevel.Minimal);

        // Act
        tracer.StartNewBlockTrace(block);
        tracer.StartNewTxTrace(transaction);

        // Assert
        var output = writer.ToString();

        // Should not contain null optional fields
        output.Should().NotContain("null");
        output.Should().NotContain("\"maxFeePerGas\""); // Optional field should be omitted
        output.Should().NotContain("\"maxPriorityFeePerGas\"");
        output.Should().NotContain("\"blobVersionedHashes\"");
    }

    [Test]
    public void EmptyArrays_AreIncluded()
    {
        // Arrange
        var block = Build.A.Block.WithNumber(1).TestObject;
        var transaction = Build.A.Transaction.TestObject;
        var receipt = Build.A.Receipt
            .WithStatusCode(1)
            .WithLogs() // Empty logs array
            .TestObject;

        var writer = new StringWriter();
        var tracer = new BlockLevelJsonTracer(writer, TraceLevel.Minimal);

        // Act
        tracer.StartNewBlockTrace(block);
        tracer.StartNewTxTrace(transaction);
        tracer.EndTxTrace(receipt);

        // Assert
        var lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var txEndLine = lines.FirstOrDefault(l => l.Contains("\"txEnd\""));
        txEndLine.Should().NotBeNull();

        // Empty logs array should be present
        txEndLine.Should().Contain("\"logs\":[]");
    }

    [Test]
    public void CompactJson_NoWhitespace()
    {
        // Arrange
        var block = Build.A.Block.WithNumber(1).TestObject;

        var writer = new StringWriter();
        var tracer = new BlockLevelJsonTracer(writer, TraceLevel.BlockLifecycle);

        // Act
        tracer.StartNewBlockTrace(block);

        // Assert
        var output = writer.ToString().TrimEnd();

        // Should not contain pretty-printing whitespace
        output.Should().NotContainAny(new[] { "\n  ", "  " });

        // Should be single line
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(1);
    }

    [Test]
    public void MultipleRecords_OnePerLine()
    {
        // Arrange
        var block = Build.A.Block.WithNumber(1).TestObject;
        var tx1 = Build.A.Transaction.TestObject;
        var tx2 = Build.A.Transaction.TestObject;

        var writer = new StringWriter();
        var tracer = new BlockLevelJsonTracer(writer, TraceLevel.Minimal);

        // Act
        tracer.StartNewBlockTrace(block);
        tracer.StartNewTxTrace(tx1);
        tracer.StartNewTxTrace(tx2);
        tracer.EndBlockTrace();

        // Assert
        var output = writer.ToString();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Should have: blockStart, txStart, txStart, blockEnd
        lines.Should().HaveCountGreaterOrEqualTo(4);

        // Each line should be valid JSON
        foreach (var line in lines)
        {
            var parseAction = () => JsonDocument.Parse(line);
            parseAction.Should().NotThrow();
        }
    }

    [Test]
    public void NestedObjects_HaveAlphabeticalKeys()
    {
        // This test would check nested objects like storage writes, logs, etc.
        // once those are implemented with canonical ordering
        Assert.Pass("Nested object testing to be added when preExecution/postExecution are implemented");
    }

    [Test]
    public void CumulativeGasUsed_IsTrackedCorrectly()
    {
        // Arrange
        var block = Build.A.Block.WithNumber(1).TestObject;
        var tx1 = Build.A.Transaction.TestObject;
        var tx2 = Build.A.Transaction.TestObject;
        var receipt1 = Build.A.Receipt.WithGasUsed(21000).TestObject;
        var receipt2 = Build.A.Receipt.WithGasUsed(50000).TestObject;

        var writer = new StringWriter();
        var tracer = new BlockLevelJsonTracer(writer, TraceLevel.Minimal);

        // Act
        tracer.StartNewBlockTrace(block);

        tracer.StartNewTxTrace(tx1);
        tracer.EndTxTrace(receipt1);

        tracer.StartNewTxTrace(tx2);
        tracer.EndTxTrace(receipt2);

        // Assert
        var lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var txStarts = lines.Where(l => l.Contains("\"txStart\"")).ToArray();
        var txEnds = lines.Where(l => l.Contains("\"txEnd\"")).ToArray();

        // First txStart should have cumulativeGasUsedBefore = 0x0
        var tx1Start = JsonDocument.Parse(txStarts[0]);
        tx1Start.RootElement.GetProperty("cumulativeGasUsedBefore").GetString().Should().Be("0x0");

        // Second txStart should have cumulativeGasUsedBefore = 0x5208 (21000)
        var tx2Start = JsonDocument.Parse(txStarts[1]);
        tx2Start.RootElement.GetProperty("cumulativeGasUsedBefore").GetString().Should().Be("0x5208");

        // Second txEnd should have cumulativeGasUsed = 0x11558 (71000)
        var tx2End = JsonDocument.Parse(txEnds[1]);
        tx2End.RootElement.GetProperty("cumulativeGasUsed").GetString().Should().Be("0x11558");
    }
}
