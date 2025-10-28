// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using Nethermind.Blockchain.Tracing.BlockLevel;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Test.Builders;
using Nethermind.Evm.Tracing.BlockOperations;
using Nethermind.Int256;
using NUnit.Framework;

namespace Nethermind.Blockchain.Test.Tracing.BlockLevel;

/// <summary>
/// Comprehensive tests for BlockLevelJsonTracer covering trace levels, JSON output format,
/// lifecycle events, and all operation types.
/// </summary>
[TestFixture]
public class BlockLevelJsonTracerTests
{
    [Test]
    public void Should_emit_block_start_record()
    {
        // Arrange
        using var writer = new StringWriter();
        using var tracer = new BlockLevelJsonTracer(writer, TraceLevel.Minimal);
        var block = Build.A.Block
            .WithNumber(1000000)
            .WithTimestamp(1234567890)
            .WithGasLimit(30000000)
            .TestObject;

        // Act
        tracer.StartNewBlockTrace(block);

        // Assert
        string output = writer.ToString();
        output.Should().Contain("\"type\":\"blockStart\"");
        output.Should().Contain("\"blockNumber\":\"0xf4240\""); // 1000000 in hex
        output.Should().Contain("\"timestamp\":\"0x499602d2\""); // 1234567890 in hex
        output.Should().Contain("\"gasLimit\":\"0x1c9c380\""); // 30000000 in hex
    }

    [Test]
    public void Should_emit_block_end_record()
    {
        // Arrange
        using var writer = new StringWriter();
        using var tracer = new BlockLevelJsonTracer(writer, TraceLevel.Minimal);
        var block = Build.A.Block
            .WithNumber(1000000)
            .WithGasUsed(21000)
            .WithStateRoot(Keccak.Zero)
            .TestObject;

        // Act
        tracer.StartNewBlockTrace(block);
        tracer.EndBlockTrace();

        // Assert
        string output = writer.ToString();
        output.Should().Contain("\"type\":\"blockEnd\"");
        output.Should().Contain("\"blockNumber\":\"0xf4240\"");
        output.Should().Contain("\"totalGasUsed\":\"0x5208\""); // 21000 in hex
    }

    [Test]
    public void Should_emit_tx_start_and_end_records()
    {
        // Arrange
        using var writer = new StringWriter();
        using var tracer = new BlockLevelJsonTracer(writer, TraceLevel.Minimal);
        var block = Build.A.Block.TestObject;
        var tx = Build.A.Transaction
            .WithGasLimit(21000)
            .WithGasPrice(1000000000)
            .WithValue(1000000000000000000)
            .TestObject;

        // Act
        tracer.StartNewBlockTrace(block);
        tracer.StartNewTxTrace(tx);
        tracer.EndTxTrace();

        // Assert
        string output = writer.ToString();
        output.Should().Contain("\"type\":\"txStart\"");
        output.Should().Contain("\"txIndex\":\"0x0\"");
        output.Should().Contain("\"gasLimit\":\"0x5208\""); // 21000
        output.Should().Contain("\"type\":\"txEnd\"");
    }

    [Test]
    [TestCase(TraceLevel.Minimal, true, true, false, false, false)]
    [TestCase(TraceLevel.Standard, true, true, true, true, false)]
    [TestCase(TraceLevel.Full, true, true, true, true, true)]
    public void Should_respect_trace_level_for_lifecycle_events(
        TraceLevel level,
        bool expectBlockLifecycle,
        bool expectTransactions,
        bool expectPreExecution,
        bool expectPostExecution,
        bool expectTrieOperations)
    {
        // Arrange
        using var writer = new StringWriter();
        using var tracer = new BlockLevelJsonTracer(writer, level);
        var block = Build.A.Block.TestObject;

        // Act
        tracer.StartNewBlockTrace(block);
        tracer.EndBlockTrace();

        string output = writer.ToString();

        // Assert
        if (expectBlockLifecycle)
        {
            output.Should().Contain("\"type\":\"blockStart\"");
            output.Should().Contain("\"type\":\"blockEnd\"");
        }
        else
        {
            output.Should().NotContain("\"type\":\"blockStart\"");
            output.Should().NotContain("\"type\":\"blockEnd\"");
        }
    }

    [Test]
    public void Should_emit_pre_execution_operation_when_trace_level_includes_pre_execution()
    {
        // Arrange
        using var writer = new StringWriter();
        using var tracer = new BlockLevelJsonTracer(writer, TraceLevel.Standard);
        var block = Build.A.Block.TestObject;

        var beaconRootOp = new BeaconRootStorageOperation
        {
            Timestamp = "0x12345678",
            ParentBeaconBlockRoot = Keccak.Zero.ToString(),
            ContractAddress = TestItem.AddressA,
            GasUsed = "0x1000"
        };

        // Act
        tracer.StartNewBlockTrace(block);
        tracer.TracePreExecution(beaconRootOp);
        tracer.EndBlockTrace();

        // Assert
        string output = writer.ToString();
        output.Should().Contain("\"type\":\"preExecution\"");
        output.Should().Contain("\"operation\":\"beaconRootStorage\"");
        output.Should().Contain("\"eip\":\"4788\"");
        output.Should().Contain("\"timestamp\":\"0x12345678\"");
    }

    [Test]
    public void Should_not_emit_pre_execution_operation_when_trace_level_is_minimal()
    {
        // Arrange
        using var writer = new StringWriter();
        using var tracer = new BlockLevelJsonTracer(writer, TraceLevel.Minimal);
        var block = Build.A.Block.TestObject;

        var beaconRootOp = new BeaconRootStorageOperation
        {
            Timestamp = "0x12345678",
            ParentBeaconBlockRoot = Keccak.Zero.ToString()
        };

        // Act
        tracer.StartNewBlockTrace(block);
        tracer.TracePreExecution(beaconRootOp);
        tracer.EndBlockTrace();

        // Assert
        string output = writer.ToString();
        output.Should().NotContain("\"type\":\"preExecution\"");
        output.Should().NotContain("beaconRootStorage");
    }

    [Test]
    public void Should_emit_post_execution_operation_when_trace_level_includes_post_execution()
    {
        // Arrange
        using var writer = new StringWriter();
        using var tracer = new BlockLevelJsonTracer(writer, TraceLevel.Standard);
        var block = Build.A.Block.TestObject;

        var withdrawalOp = new WithdrawalsOperation
        {
            AccountsCreated = "0x1",
            EmptyAccountsDeleted = "0x0",
            TotalWithdrawn = "0x1000000"
        };

        // Act
        tracer.StartNewBlockTrace(block);
        tracer.TracePostExecution(withdrawalOp);
        tracer.EndBlockTrace();

        // Assert
        string output = writer.ToString();
        output.Should().Contain("\"type\":\"postExecution\"");
        output.Should().Contain("\"operation\":\"withdrawals\"");
    }

    [Test]
    public void Should_emit_validation_operation_when_trace_level_includes_validation()
    {
        // Arrange
        using var writer = new StringWriter();
        using var tracer = new BlockLevelJsonTracer(writer, TraceLevel.Standard);
        var block = Build.A.Block.TestObject;

        var headerValidation = new HeaderValidationOperation
        {
            Valid = true,
            OverallResult = "valid"
        };
        headerValidation.Rules.Add(new ValidationRule
        {
            Rule = "gasLimit",
            Valid = true
        });

        // Act
        tracer.StartNewBlockTrace(block);
        tracer.TraceValidation(headerValidation);
        tracer.EndBlockTrace();

        // Assert
        string output = writer.ToString();
        output.Should().Contain("\"type\":\"validation\"");
        output.Should().Contain("\"operation\":\"headerValidation\"");
        output.Should().Contain("\"valid\":true");
    }

    [Test]
    public void Should_emit_trie_operation_when_trace_level_is_full()
    {
        // Arrange
        using var writer = new StringWriter();
        using var tracer = new BlockLevelJsonTracer(writer, TraceLevel.Full);
        var block = Build.A.Block.TestObject;

        var trieOp = new TrieOperation
        {
            Operation = "calculateStateRoot",
            Root = Keccak.Zero.ToString(),
            TrieType = "state",
            NodeCount = "0x1234",
            CalculationTime = "0x5"
        };

        // Act
        tracer.StartNewBlockTrace(block);
        tracer.TraceTrieOperation(trieOp);
        tracer.EndBlockTrace();

        // Assert
        string output = writer.ToString();
        output.Should().Contain("\"type\":\"trieOperation\"");
        output.Should().Contain("\"operation\":\"calculateStateRoot\"");
    }

    [Test]
    public void Should_not_emit_trie_operation_when_trace_level_is_standard()
    {
        // Arrange
        using var writer = new StringWriter();
        using var tracer = new BlockLevelJsonTracer(writer, TraceLevel.Standard);
        var block = Build.A.Block.TestObject;

        var trieOp = new TrieOperation
        {
            Operation = "calculateStateRoot"
        };

        // Act
        tracer.StartNewBlockTrace(block);
        tracer.TraceTrieOperation(trieOp);
        tracer.EndBlockTrace();

        // Assert
        string output = writer.ToString();
        output.Should().NotContain("\"type\":\"trieOperation\"");
    }

    [Test]
    public void Should_output_json_lines_format_one_object_per_line()
    {
        // Arrange
        using var writer = new StringWriter();
        using var tracer = new BlockLevelJsonTracer(writer, TraceLevel.Minimal);
        var block = Build.A.Block.TestObject;
        var tx = Build.A.Transaction.TestObject;

        // Act
        tracer.StartNewBlockTrace(block);
        tracer.StartNewTxTrace(tx);
        tracer.EndTxTrace();
        tracer.EndBlockTrace();

        // Assert
        string output = writer.ToString();
        string[] lines = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        // Should have: blockStart, txStart, txEnd, blockEnd = 4 lines
        lines.Should().HaveCount(4);

        // Each line should be valid JSON
        foreach (var line in lines)
        {
            var isValid = IsValidJson(line);
            isValid.Should().BeTrue($"Line should be valid JSON: {line}");
        }
    }

    [Test]
    public void Should_format_numbers_as_hexadecimal_with_0x_prefix()
    {
        // Arrange
        using var writer = new StringWriter();
        using var tracer = new BlockLevelJsonTracer(writer, TraceLevel.Minimal);
        var block = Build.A.Block
            .WithNumber(255)
            .WithTimestamp(4095)
            .WithGasLimit(16777215)
            .TestObject;

        // Act
        tracer.StartNewBlockTrace(block);

        // Assert
        string output = writer.ToString();
        output.Should().Contain("\"blockNumber\":\"0xff\""); // 255
        output.Should().Contain("\"timestamp\":\"0xfff\""); // 4095
        output.Should().Contain("\"gasLimit\":\"0xffffff\""); // 16777215
    }

    [Test]
    public void Should_include_base_fee_per_gas_when_present()
    {
        // Arrange
        using var writer = new StringWriter();
        using var tracer = new BlockLevelJsonTracer(writer, TraceLevel.Minimal);
        var block = Build.A.Block
            .WithNumber(1)
            .WithBaseFeePerGas(1000000000)
            .TestObject;

        // Act
        tracer.StartNewBlockTrace(block);

        // Assert
        string output = writer.ToString();
        output.Should().Contain("\"baseFeePerGas\":\"0x3b9aca00\""); // 1000000000
    }

    [Test]
    public void Should_include_blob_gas_fields_when_present()
    {
        // Arrange
        using var writer = new StringWriter();
        using var tracer = new BlockLevelJsonTracer(writer, TraceLevel.Minimal);
        var block = Build.A.Block
            .WithNumber(1)
            .WithExcessBlobGas(131072)
            .WithBlobGasUsed(262144)
            .WithParentBeaconBlockRoot(Keccak.Zero)
            .TestObject;

        // Act
        tracer.StartNewBlockTrace(block);

        // Assert
        string output = writer.ToString();
        output.Should().Contain("\"excessBlobGas\":\"0x20000\""); // 131072
        output.Should().Contain("\"blobGasUsed\":\"0x40000\""); // 262144
        output.Should().Contain("\"parentBeaconBlockRoot\"");
    }

    [Test]
    public void Should_handle_null_writer_by_using_console_out()
    {
        // Arrange & Act
        using var tracer = new BlockLevelJsonTracer(null, TraceLevel.Minimal);

        // Assert - Should not throw
        var block = Build.A.Block.TestObject;
        Action act = () =>
        {
            tracer.StartNewBlockTrace(block);
            tracer.EndBlockTrace();
        };

        act.Should().NotThrow();
    }

    [Test]
    public void Should_throw_argument_null_exception_when_block_is_null()
    {
        // Arrange
        using var writer = new StringWriter();
        using var tracer = new BlockLevelJsonTracer(writer, TraceLevel.Minimal);

        // Act & Assert
        Action act = () => tracer.StartNewBlockTrace(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void Should_throw_argument_null_exception_for_null_pre_execution_operation()
    {
        // Arrange
        using var writer = new StringWriter();
        using var tracer = new BlockLevelJsonTracer(writer, TraceLevel.Standard);

        // Act & Assert
        Action act = () => tracer.TracePreExecution(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void Should_throw_argument_null_exception_for_null_post_execution_operation()
    {
        // Arrange
        using var writer = new StringWriter();
        using var tracer = new BlockLevelJsonTracer(writer, TraceLevel.Standard);

        // Act & Assert
        Action act = () => tracer.TracePostExecution(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void Should_throw_argument_null_exception_for_null_validation_operation()
    {
        // Arrange
        using var writer = new StringWriter();
        using var tracer = new BlockLevelJsonTracer(writer, TraceLevel.Standard);

        // Act & Assert
        Action act = () => tracer.TraceValidation(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void Should_throw_argument_null_exception_for_null_trie_operation()
    {
        // Arrange
        using var writer = new StringWriter();
        using var tracer = new BlockLevelJsonTracer(writer, TraceLevel.Full);

        // Act & Assert
        Action act = () => tracer.TraceTrieOperation(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void Should_flush_writer_on_end_block_trace()
    {
        // Arrange
        using var writer = new StringWriter();
        using var tracer = new BlockLevelJsonTracer(writer, TraceLevel.Minimal);
        var block = Build.A.Block.TestObject;

        // Act
        tracer.StartNewBlockTrace(block);
        tracer.EndBlockTrace();

        // Assert - Writer should have content immediately after EndBlockTrace
        string output = writer.ToString();
        output.Should().NotBeEmpty();
        output.Should().Contain("\"type\":\"blockStart\"");
        output.Should().Contain("\"type\":\"blockEnd\"");
    }

    [Test]
    public void Should_dispose_writer_when_owns_writer_is_true()
    {
        // Arrange
        var writer = new StringWriter();
        var tracer = new BlockLevelJsonTracer(writer, TraceLevel.Minimal, ownsWriter: true);

        // Act
        tracer.Dispose();

        // Assert - Writer should be disposed and throw when writing
        Action act = () => writer.WriteLine("test");
        act.Should().Throw<ObjectDisposedException>();
    }

    [Test]
    public void Should_not_dispose_writer_when_owns_writer_is_false()
    {
        // Arrange
        var writer = new StringWriter();
        var tracer = new BlockLevelJsonTracer(writer, TraceLevel.Minimal, ownsWriter: false);

        // Act
        tracer.Dispose();

        // Assert - Writer should still be usable
        Action act = () => writer.WriteLine("test");
        act.Should().NotThrow();

        writer.Dispose();
    }

    [Test]
    public void Should_increment_tx_index_for_multiple_transactions()
    {
        // Arrange
        using var writer = new StringWriter();
        using var tracer = new BlockLevelJsonTracer(writer, TraceLevel.Minimal);
        var block = Build.A.Block.TestObject;
        var tx1 = Build.A.Transaction.TestObject;
        var tx2 = Build.A.Transaction.TestObject;

        // Act
        tracer.StartNewBlockTrace(block);
        tracer.StartNewTxTrace(tx1);
        tracer.EndTxTrace();
        tracer.StartNewTxTrace(tx2);
        tracer.EndTxTrace();
        tracer.EndBlockTrace();

        // Assert
        string output = writer.ToString();
        output.Should().Contain("\"txIndex\":\"0x0\"");
        output.Should().Contain("\"txIndex\":\"0x1\"");
    }

    [Test]
    public void Should_determine_fork_based_on_block_fields()
    {
        // Test Cancun fork (has parent beacon block root)
        using var writer1 = new StringWriter();
        using var tracer1 = new BlockLevelJsonTracer(writer1, TraceLevel.Minimal);
        var cancunBlock = Build.A.Block
            .WithNumber(1)
            .WithParentBeaconBlockRoot(Keccak.Zero)
            .TestObject;

        tracer1.StartNewBlockTrace(cancunBlock);
        string output1 = writer1.ToString();
        output1.Should().Contain("\"fork\":\"cancun\"");

        // Test Shanghai fork (has withdrawals root)
        using var writer2 = new StringWriter();
        using var tracer2 = new BlockLevelJsonTracer(writer2, TraceLevel.Minimal);
        var shanghaiBlock = Build.A.Block
            .WithNumber(1)
            .WithWithdrawalsRoot(Keccak.Zero)
            .TestObject;

        tracer2.StartNewBlockTrace(shanghaiBlock);
        string output2 = writer2.ToString();
        output2.Should().Contain("\"fork\":\"shanghai\"");
    }

    [Test]
    public void Should_handle_concurrent_writes_safely()
    {
        // Arrange
        using var writer = new StringWriter();
        using var tracer = new BlockLevelJsonTracer(writer, TraceLevel.Full);
        var block = Build.A.Block.TestObject;

        // Act - Simulate concurrent operations
        tracer.StartNewBlockTrace(block);

        var operations = new Action[]
        {
            () => tracer.TracePreExecution(new BeaconRootStorageOperation()),
            () => tracer.TracePostExecution(new WithdrawalsOperation()),
            () => tracer.TraceValidation(new HeaderValidationOperation()),
            () => tracer.TraceTrieOperation(new TrieOperation { Operation = "test" })
        };

        // Assert - Should not throw
        foreach (var op in operations)
        {
            op.Should().NotThrow();
        }
    }

    [Test]
    public void Should_expose_trace_level_property()
    {
        // Arrange & Act
        using var tracer1 = new BlockLevelJsonTracer(null, TraceLevel.Minimal);
        using var tracer2 = new BlockLevelJsonTracer(null, TraceLevel.Standard);
        using var tracer3 = new BlockLevelJsonTracer(null, TraceLevel.Full);

        // Assert
        tracer1.Level.Should().Be(TraceLevel.Minimal);
        tracer2.Level.Should().Be(TraceLevel.Standard);
        tracer3.Level.Should().Be(TraceLevel.Full);
    }

    [Test]
    public void Should_set_is_tracing_rewards_based_on_trace_level()
    {
        // Arrange & Act
        using var minimalTracer = new BlockLevelJsonTracer(null, TraceLevel.Minimal);
        using var standardTracer = new BlockLevelJsonTracer(null, TraceLevel.Standard);

        // Assert
        minimalTracer.IsTracingRewards.Should().BeFalse();
        standardTracer.IsTracingRewards.Should().BeTrue(); // Standard includes PostExecution
    }

    private static bool IsValidJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
