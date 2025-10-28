// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using FluentAssertions;
using Nethermind.Blockchain.Tracing.BlockLevel;
using Nethermind.Int256;
using NUnit.Framework;

namespace Nethermind.Blockchain.Test.Tracing.BlockLevel;

/// <summary>
/// Tests for storage value padding in preExecution traces (EIP-2935, EIP-4788).
/// Verifies that storage values are always padded to 32 bytes (64 hex characters).
/// </summary>
[TestFixture]
public class StorageValuePaddingTests
{
    [Test]
    public void UInt256ToHashHex_ZeroValue_PadsTo32Bytes()
    {
        // Arrange
        var value = UInt256.Zero;

        // Act
        string result = CanonicalFormatHelpers.UInt256ToHashHex(value);

        // Assert
        result.Should().Be("0x0000000000000000000000000000000000000000000000000000000000000000");
        result.Length.Should().Be(66); // 0x + 64 hex chars
    }

    [Test]
    public void UInt256ToHashHex_SmallValue_PadsTo32Bytes()
    {
        // Arrange
        var value = new UInt256(0xabc);

        // Act
        string result = CanonicalFormatHelpers.UInt256ToHashHex(value);

        // Assert
        result.Should().Be("0x0000000000000000000000000000000000000000000000000000000000000abc");
        result.Length.Should().Be(66); // 0x + 64 hex chars
    }

    [Test]
    public void UInt256ToHashHex_MediumValue_PadsTo32Bytes()
    {
        // Arrange
        var value = new UInt256(0x123456789abcdef0);

        // Act
        string result = CanonicalFormatHelpers.UInt256ToHashHex(value);

        // Assert
        result.Should().Be("0x000000000000000000000000000000000000000000000000123456789abcdef0");
        result.Length.Should().Be(66); // 0x + 64 hex chars
    }

    [Test]
    public void UInt256ToHashHex_FullValue_Is32Bytes()
    {
        // Arrange - Create a full 32-byte value
        var bytes = new byte[32];
        for (int i = 0; i < 32; i++)
        {
            bytes[i] = (byte)(i + 1);
        }
        var value = new UInt256(bytes, isBigEndian: true);

        // Act
        string result = CanonicalFormatHelpers.UInt256ToHashHex(value);

        // Assert
        result.Should().Be("0x0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f20");
        result.Length.Should().Be(66); // 0x + 64 hex chars
    }

    [Test]
    public void UInt256ToHashHex_MaxValue_Is32Bytes()
    {
        // Arrange
        var value = UInt256.MaxValue;

        // Act
        string result = CanonicalFormatHelpers.UInt256ToHashHex(value);

        // Assert
        result.Should().Be("0xffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff");
        result.Length.Should().Be(66); // 0x + 64 hex chars
    }

    [Test]
    public void UInt256ToHashHex_OutputIsLowercase()
    {
        // Arrange - Value with hex digits that could be uppercase
        var value = new UInt256(0xABCDEF);

        // Act
        string result = CanonicalFormatHelpers.UInt256ToHashHex(value);

        // Assert
        result.Should().Contain("abcdef");
        result.Should().NotContain("ABCDEF");
        CanonicalFormatHelpers.IsCanonicalHex(result).Should().BeTrue();
    }
}
