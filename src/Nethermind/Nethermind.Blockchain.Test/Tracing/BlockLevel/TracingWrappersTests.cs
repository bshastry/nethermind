// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using FluentAssertions;
using Nethermind.Blockchain.Tracing.BlockLevel;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Test.Builders;
using Nethermind.Evm.Tracing;
using Nethermind.Evm.Tracing.BlockOperations;
using Nethermind.Int256;
using NSubstitute;
using NUnit.Framework;

namespace Nethermind.Blockchain.Test.Tracing.BlockLevel;

/// <summary>
/// Comprehensive tests for tracing wrapper processors that wrap block processing operations
/// to emit trace records including system calls, withdrawals, execution requests, header validation,
/// and gas accounting.
/// </summary>
[TestFixture]
public class TracingWrappersTests
{
    #region TracingSystemCallProcessor Tests

    [Test]
    public void TracingSystemCallProcessor_should_throw_when_tracer_is_null()
    {
        // Act & Assert
        Action act = () => new TracingSystemCallProcessor(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void TracingSystemCallProcessor_should_emit_beacon_root_storage_trace()
    {
        // Arrange
        var tracer = Substitute.For<IBlockTracer>();
        var processor = new TracingSystemCallProcessor(tracer);

        var header = Build.A.BlockHeader
            .WithNumber(1000000)
            .WithTimestamp(1234567890)
            .TestObject;

        var parentBeaconBlockRoot = Keccak.Compute("test");
        var contractAddress = new Address("0x000F3df6D732807Ef1319fB7B8bB8522d0Beac02");

        var writes = new[]
        {
            (slot: (UInt256)100, oldValue: UInt256.Zero, newValue: (UInt256)1234567890),
            (slot: (UInt256)101, oldValue: UInt256.Zero, newValue: new UInt256(parentBeaconBlockRoot.Bytes))
        };

        // Act
        processor.TraceBeaconRootStorage(header, parentBeaconBlockRoot, contractAddress, writes, gasUsed: 5000);

        // Assert
        tracer.Received(1).TracePreExecution(Arg.Is<BeaconRootStorageOperation>(op =>
            op.Operation == "beaconRootStorage" &&
            op.Eip == "4788" &&
            op.Timestamp == "0x499602d2" &&
            op.ParentBeaconBlockRoot == parentBeaconBlockRoot.ToString() &&
            op.ContractAddress == contractAddress &&
            op.GasUsed == "0x1388" &&
            op.StorageWrites.Count == 2
        ));
    }

    [Test]
    public void TracingSystemCallProcessor_should_calculate_beacon_root_ring_buffer_index()
    {
        // Arrange
        var tracer = Substitute.For<IBlockTracer>();
        var processor = new TracingSystemCallProcessor(tracer);

        ulong timestamp = 1234567890;
        const int historyBufferLength = 8191;
        ulong expectedIndex = timestamp % historyBufferLength;
        ulong expectedTimestampSlot = expectedIndex; // Per EIP-4788: slot = index
        ulong expectedRootSlot = expectedIndex + historyBufferLength; // Per EIP-4788: slot = index + 8191

        var header = Build.A.BlockHeader.WithTimestamp(timestamp).TestObject;
        var parentBeaconBlockRoot = Keccak.Zero;
        var contractAddress = new Address("0x000F3df6D732807Ef1319fB7B8bB8522d0Beac02");

        // Act
        processor.TraceBeaconRootStorage(header, parentBeaconBlockRoot, contractAddress, Array.Empty<(UInt256, UInt256, UInt256)>(), 0);

        // Assert
        tracer.Received(1).TracePreExecution(Arg.Is<BeaconRootStorageOperation>(op =>
            op.RingBuffer.Index == $"0x{expectedIndex:x}" &&
            op.RingBuffer.TimestampSlot == $"0x{expectedTimestampSlot:x}" &&
            op.RingBuffer.RootSlot == $"0x{expectedRootSlot:x}"
        ));
    }

    [Test]
    public void TracingSystemCallProcessor_should_handle_null_beacon_root_gracefully()
    {
        // Arrange
        var tracer = Substitute.For<IBlockTracer>();
        var processor = new TracingSystemCallProcessor(tracer);
        var header = Build.A.BlockHeader.TestObject;
        var contractAddress = new Address("0x000F3df6D732807Ef1319fB7B8bB8522d0Beac02");

        // Act
        processor.TraceBeaconRootStorage(header, null!, contractAddress, Array.Empty<(UInt256, UInt256, UInt256)>(), 0);

        // Assert - Should not call tracer
        tracer.DidNotReceive().TracePreExecution(Arg.Any<PreExecutionOperation>());
    }

    [Test]
    public void TracingSystemCallProcessor_should_emit_block_hash_storage_trace()
    {
        // Arrange
        var tracer = Substitute.For<IBlockTracer>();
        var processor = new TracingSystemCallProcessor(tracer);

        long blockNumber = 1000000;
        var parentHash = Keccak.Compute("parent");
        var contractAddress = new Address("0x0000000000000000000000000000000000002935");
        var write = (slot: (UInt256)123, oldValue: UInt256.Zero, newValue: new UInt256(parentHash.Bytes));

        // Act
        processor.TraceBlockHashStorage(blockNumber, parentHash, contractAddress, write, gasUsed: 3000);

        // Assert
        tracer.Received(1).TracePreExecution(Arg.Is<BlockHashStorageOperation>(op =>
            op.Operation == "blockHashStorage" &&
            op.Eip == "2935" &&
            op.BlockNumber == "0xf4240" &&
            op.ParentHash == parentHash.ToString() &&
            op.ContractAddress == contractAddress &&
            op.GasUsed == "0xbb8" &&
            op.StorageWrites.Count == 1
        ));
    }

    [Test]
    public void TracingSystemCallProcessor_should_calculate_block_hash_ring_buffer_index()
    {
        // Arrange
        var tracer = Substitute.For<IBlockTracer>();
        var processor = new TracingSystemCallProcessor(tracer);

        long blockNumber = 1000000;
        long parentBlockNumber = blockNumber - 1;
        long expectedIndex = parentBlockNumber % 8191;

        var parentHash = Keccak.Zero;
        var contractAddress = new Address("0x0000000000000000000000000000000000002935");
        var write = (slot: (UInt256)expectedIndex, oldValue: UInt256.Zero, newValue: UInt256.One);

        // Act
        processor.TraceBlockHashStorage(blockNumber, parentHash, contractAddress, write, 0);

        // Assert
        tracer.Received(1).TracePreExecution(Arg.Is<BlockHashStorageOperation>(op =>
            op.RingBuffer.Index == $"0x{expectedIndex:x}" &&
            op.RingBuffer.Slot == $"0x{expectedIndex:x}"
        ));
    }

    [Test]
    public void TracingSystemCallProcessor_should_handle_negative_ring_buffer_index()
    {
        // Arrange
        var tracer = Substitute.For<IBlockTracer>();
        var processor = new TracingSystemCallProcessor(tracer);

        long blockNumber = 5;
        long parentBlockNumber = blockNumber - 1; // 4
        long expectedIndex = (parentBlockNumber % 8191 + 8191) % 8191; // Handle negative modulo

        var parentHash = Keccak.Zero;
        var contractAddress = new Address("0x0000000000000000000000000000000000002935");
        var write = (slot: (UInt256)0, oldValue: UInt256.Zero, newValue: UInt256.One);

        // Act
        processor.TraceBlockHashStorage(blockNumber, parentHash, contractAddress, write, 0);

        // Assert - Should handle correctly without throwing
        tracer.Received(1).TracePreExecution(Arg.Any<BlockHashStorageOperation>());
    }

    #endregion

    #region TracingWithdrawalsProcessor Tests

    [Test]
    public void TracingWithdrawalsProcessor_should_throw_when_tracer_is_null()
    {
        // Act & Assert
        Action act = () => new TracingWithdrawalsProcessor(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void TracingWithdrawalsProcessor_should_emit_withdrawals_trace()
    {
        // Arrange
        var tracer = Substitute.For<IBlockTracer>();
        var processor = new TracingWithdrawalsProcessor(tracer);

        var withdrawals = new[]
        {
            Build.A.Withdrawal
                .WithIndex(0)
                .WithValidatorIndex(12345)
                .WithRecipient(TestItem.AddressA)
                .WithAmount(1000000)
                .TestObject,
            Build.A.Withdrawal
                .WithIndex(1)
                .WithValidatorIndex(67890)
                .WithRecipient(TestItem.AddressB)
                .WithAmount(2000000)
                .TestObject
        };

        var balanceChanges = new Dictionary<Address, (UInt256 before, UInt256 after)>
        {
            [TestItem.AddressA] = (UInt256.Zero, (UInt256)1000000 * 1_000_000_000),
            [TestItem.AddressB] = ((UInt256)500, (UInt256)2000000 * 1_000_000_000 + 500)
        };

        // Act
        processor.TraceWithdrawals(withdrawals, balanceChanges, accountsCreated: 1, emptyAccountsDeleted: 0);

        // Assert
        tracer.Received(1).TracePostExecution(Arg.Is<WithdrawalsOperation>(op =>
            op.Operation == "withdrawals" &&
            op.Withdrawals.Count == 2 &&
            op.AccountsCreated == "0x1" &&
            op.EmptyAccountsDeleted == "0x0"
        ));
    }

    [Test]
    public void TracingWithdrawalsProcessor_should_use_canonical_format()
    {
        // Arrange
        var tracer = Substitute.For<IBlockTracer>();
        var processor = new TracingWithdrawalsProcessor(tracer);

        ulong amountInGwei = 1000;

        var withdrawals = new[]
        {
            Build.A.Withdrawal
                .WithIndex(0)
                .WithValidatorIndex(100)
                .WithRecipient(TestItem.AddressA)
                .WithAmount(amountInGwei)
                .TestObject
        };

        // Act
        processor.TraceWithdrawals(withdrawals, null, 0, 0);

        // Assert - Canonical format uses only AmountGwei (not AmountWei or GweiToWei)
        tracer.Received(1).TracePostExecution(Arg.Is<WithdrawalsOperation>(op =>
            op.Withdrawals[0].AmountGwei == $"0x{amountInGwei:x}"
        ));
    }

    [Test]
    public void TracingWithdrawalsProcessor_should_calculate_total_withdrawn()
    {
        // Arrange
        var tracer = Substitute.For<IBlockTracer>();
        var processor = new TracingWithdrawalsProcessor(tracer);

        var withdrawals = new[]
        {
            Build.A.Withdrawal.WithAmount(1000).TestObject,
            Build.A.Withdrawal.WithAmount(2000).TestObject,
            Build.A.Withdrawal.WithAmount(3000).TestObject
        };

        ulong expectedTotalGwei = 6000;

        // Act
        processor.TraceWithdrawals(withdrawals, null, 0, 0);

        // Assert - TotalWithdrawn is in Gwei (canonical format)
        tracer.Received(1).TracePostExecution(Arg.Is<WithdrawalsOperation>(op =>
            op.TotalWithdrawn == $"0x{expectedTotalGwei:x}"
        ));
    }

    [Test]
    public void TracingWithdrawalsProcessor_should_handle_null_withdrawals()
    {
        // Arrange
        var tracer = Substitute.For<IBlockTracer>();
        var processor = new TracingWithdrawalsProcessor(tracer);

        // Act
        processor.TraceWithdrawals(null, null, 0, 0);

        // Assert - Should not call tracer
        tracer.DidNotReceive().TracePostExecution(Arg.Any<PostExecutionOperation>());
    }

    [Test]
    public void TracingWithdrawalsProcessor_should_handle_empty_withdrawals_array()
    {
        // Arrange
        var tracer = Substitute.For<IBlockTracer>();
        var processor = new TracingWithdrawalsProcessor(tracer);

        // Act
        processor.TraceWithdrawals(Array.Empty<Withdrawal>(), null, 0, 0);

        // Assert - Should not call tracer
        tracer.DidNotReceive().TracePostExecution(Arg.Any<PostExecutionOperation>());
    }

    [Test]
    public void TracingWithdrawalsProcessor_should_use_balance_change_callbacks()
    {
        // Arrange
        var tracer = Substitute.For<IBlockTracer>();
        var processor = new TracingWithdrawalsProcessor(tracer);

        var withdrawal = Build.A.Withdrawal
            .WithRecipient(TestItem.AddressA)
            .WithAmount(1000)
            .TestObject;

        UInt256 balanceBefore = 100;
        UInt256 balanceAfter = 1_000_000_000_000 + 100;

        // Act
        processor.TraceWithdrawals(
            new[] { withdrawal },
            addr => balanceBefore,
            addr => balanceAfter,
            0,
            0);

        // Assert
        tracer.Received(1).TracePostExecution(Arg.Is<WithdrawalsOperation>(op =>
            op.Withdrawals[0].BalanceChange!.Before == $"0x{balanceBefore:x}" &&
            op.Withdrawals[0].BalanceChange.After == $"0x{balanceAfter:x}"
        ));
    }

    #endregion

    #region TracingExecutionRequestsProcessor Tests

    [Test]
    public void TracingExecutionRequestsProcessor_should_throw_when_tracer_is_null()
    {
        // Act & Assert
        Action act = () => new TracingExecutionRequestsProcessor(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void TracingExecutionRequestsProcessor_should_parse_deposit_request()
    {
        // Arrange
        var tracer = Substitute.For<IBlockTracer>();
        var processor = new TracingExecutionRequestsProcessor(tracer);

        // Create a deposit request (type 0x00)
        byte[] depositRequest = new byte[193];
        depositRequest[0] = 0x00; // Request type
        Array.Fill<byte>(depositRequest, 0xAA, 1, 48); // Pubkey
        Array.Fill<byte>(depositRequest, 0xBB, 49, 32); // Withdrawal credentials
        Array.Fill<byte>(depositRequest, 0xCC, 81, 8); // Amount
        Array.Fill<byte>(depositRequest, 0xDD, 89, 96); // Signature
        Array.Fill<byte>(depositRequest, 0xEE, 185, 8); // Index

        var requests = new byte[][] { depositRequest };
        var requestsHash = Keccak.Compute("test");

        // Act
        processor.TraceExecutionRequests(requests, requestsHash);

        // Assert
        tracer.Received(1).TracePostExecution(Arg.Is<ExecutionRequestsOperation>(op =>
            op.Operation == "executionRequests" &&
            op.Requests.Count == 1 &&
            op.Requests[0].RequestType == "0x00" &&
            op.Requests[0].RequestName == "deposit" &&
            op.Requests[0].Eip == "6110" &&
            op.Requests[0].Parsing.Fields.ContainsKey("pubkey") &&
            op.Requests[0].Parsing.Fields.ContainsKey("withdrawalCredentials") &&
            op.Requests[0].Parsing.Fields.ContainsKey("amount") &&
            op.Requests[0].Parsing.Fields.ContainsKey("signature") &&
            op.Requests[0].Parsing.Fields.ContainsKey("index")
        ));
    }

    [Test]
    public void TracingExecutionRequestsProcessor_should_parse_withdrawal_request()
    {
        // Arrange
        var tracer = Substitute.For<IBlockTracer>();
        var processor = new TracingExecutionRequestsProcessor(tracer);

        // Create a withdrawal request (type 0x01)
        byte[] withdrawalRequest = new byte[77];
        withdrawalRequest[0] = 0x01; // Request type
        Array.Fill<byte>(withdrawalRequest, 0xAA, 1, 20); // Source address
        Array.Fill<byte>(withdrawalRequest, 0xBB, 21, 48); // Validator pubkey
        Array.Fill<byte>(withdrawalRequest, 0xCC, 69, 8); // Amount

        var requests = new byte[][] { withdrawalRequest };

        // Act
        processor.TraceExecutionRequests(requests, Keccak.Zero);

        // Assert
        tracer.Received(1).TracePostExecution(Arg.Is<ExecutionRequestsOperation>(op =>
            op.Requests[0].RequestType == "0x01" &&
            op.Requests[0].RequestName == "withdrawal" &&
            op.Requests[0].Eip == "7002" &&
            op.Requests[0].Parsing.Fields.ContainsKey("sourceAddress") &&
            op.Requests[0].Parsing.Fields.ContainsKey("validatorPubkey") &&
            op.Requests[0].Parsing.Fields.ContainsKey("amount")
        ));
    }

    [Test]
    public void TracingExecutionRequestsProcessor_should_parse_consolidation_request()
    {
        // Arrange
        var tracer = Substitute.For<IBlockTracer>();
        var processor = new TracingExecutionRequestsProcessor(tracer);

        // Create a consolidation request (type 0x02)
        byte[] consolidationRequest = new byte[117];
        consolidationRequest[0] = 0x02; // Request type
        Array.Fill<byte>(consolidationRequest, 0xAA, 1, 20); // Source address
        Array.Fill<byte>(consolidationRequest, 0xBB, 21, 48); // Source pubkey
        Array.Fill<byte>(consolidationRequest, 0xCC, 69, 48); // Target pubkey

        var requests = new byte[][] { consolidationRequest };

        // Act
        processor.TraceExecutionRequests(requests, Keccak.Zero);

        // Assert
        tracer.Received(1).TracePostExecution(Arg.Is<ExecutionRequestsOperation>(op =>
            op.Requests[0].RequestType == "0x02" &&
            op.Requests[0].RequestName == "consolidation" &&
            op.Requests[0].Eip == "7251" &&
            op.Requests[0].Parsing.Fields.ContainsKey("sourceAddress") &&
            op.Requests[0].Parsing.Fields.ContainsKey("sourcePubkey") &&
            op.Requests[0].Parsing.Fields.ContainsKey("targetPubkey")
        ));
    }

    [Test]
    public void TracingExecutionRequestsProcessor_should_handle_unknown_request_type()
    {
        // Arrange
        var tracer = Substitute.For<IBlockTracer>();
        var processor = new TracingExecutionRequestsProcessor(tracer);

        byte[] unknownRequest = new byte[10];
        unknownRequest[0] = 0xFF; // Unknown type

        var requests = new byte[][] { unknownRequest };

        // Act
        processor.TraceExecutionRequests(requests, Keccak.Zero);

        // Assert
        tracer.Received(1).TracePostExecution(Arg.Is<ExecutionRequestsOperation>(op =>
            op.Requests[0].RequestType == "0xff" &&
            op.Requests[0].RequestName == "unknown" &&
            op.Requests[0].Eip == "unknown"
        ));
    }

    [Test]
    public void TracingExecutionRequestsProcessor_should_include_field_offsets()
    {
        // Arrange
        var tracer = Substitute.For<IBlockTracer>();
        var processor = new TracingExecutionRequestsProcessor(tracer);

        byte[] depositRequest = new byte[193];
        depositRequest[0] = 0x00;

        var requests = new byte[][] { depositRequest };

        // Act
        processor.TraceExecutionRequests(requests, Keccak.Zero);

        // Assert
        tracer.Received(1).TracePostExecution(Arg.Is<ExecutionRequestsOperation>(op =>
            op.Requests[0].Parsing.Fields["pubkey"].Offset == "0x0" &&
            op.Requests[0].Parsing.Fields["pubkey"].Length == "0x30" && // 48 bytes
            op.Requests[0].Parsing.Fields["withdrawalCredentials"].Offset == "0x30" && // 48 in hex
            op.Requests[0].Parsing.Fields["withdrawalCredentials"].Length == "0x20" && // 32 bytes
            op.Requests[0].Parsing.Fields["amount"].Offset == "0x50" && // 80 in hex
            op.Requests[0].Parsing.Fields["amount"].Length == "0x8" // 8 bytes
        ));
    }

    [Test]
    public void TracingExecutionRequestsProcessor_should_handle_null_requests()
    {
        // Arrange
        var tracer = Substitute.For<IBlockTracer>();
        var processor = new TracingExecutionRequestsProcessor(tracer);

        // Act
        processor.TraceExecutionRequests(null, Keccak.Zero);

        // Assert - Should not call tracer
        tracer.DidNotReceive().TracePostExecution(Arg.Any<PostExecutionOperation>());
    }

    [Test]
    public void TracingExecutionRequestsProcessor_should_include_system_call_information()
    {
        // Arrange
        var tracer = Substitute.For<IBlockTracer>();
        var processor = new TracingExecutionRequestsProcessor(tracer);

        byte[] depositRequest = new byte[193];
        depositRequest[0] = 0x00;

        var systemCalls = new Dictionary<byte, (Address contractAddress, byte[] input, byte[] output, long gasUsed)>
        {
            [0x00] = (TestItem.AddressA, new byte[] { 0x01, 0x02 }, new byte[] { 0x03, 0x04 }, 5000)
        };

        // Act
        processor.TraceExecutionRequests(new byte[][] { depositRequest }, Keccak.Zero, systemCalls);

        // Assert
        tracer.Received(1).TracePostExecution(Arg.Is<ExecutionRequestsOperation>(op =>
            op.Requests[0].SystemCall != null &&
            op.Requests[0].SystemCall.ContractAddress == TestItem.AddressA &&
            op.Requests[0].SystemCall.GasUsed == "0x1388"
        ));
    }

    #endregion

    #region TracingHeaderValidator Tests

    [Test]
    public void TracingHeaderValidator_should_throw_when_tracer_is_null()
    {
        // Act & Assert
        Action act = () => new TracingHeaderValidator(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void TracingHeaderValidator_should_validate_gas_limit()
    {
        // Arrange
        var tracer = Substitute.For<IBlockTracer>();
        var validator = new TracingHeaderValidator(tracer);

        var parent = Build.A.BlockHeader.WithGasLimit(30000000).TestObject;
        var header = Build.A.BlockHeader.WithGasLimit(30029296).TestObject; // Within 1/1024 delta

        // Act
        validator.TraceHeaderValidation(header, parent, isValid: true);

        // Assert
        tracer.Received(1).TraceValidation(Arg.Is<HeaderValidationOperation>(op =>
            op.Rules.Exists(r => r.Rule == "gasLimit" && r.Valid)
        ));
    }

    [Test]
    public void TracingHeaderValidator_should_validate_timestamp()
    {
        // Arrange
        var tracer = Substitute.For<IBlockTracer>();
        var validator = new TracingHeaderValidator(tracer);

        var parent = Build.A.BlockHeader.WithTimestamp(1000).TestObject;
        var header = Build.A.BlockHeader.WithTimestamp(1012).TestObject;

        // Act
        validator.TraceHeaderValidation(header, parent, isValid: true);

        // Assert
        tracer.Received(1).TraceValidation(Arg.Is<HeaderValidationOperation>(op =>
            op.Rules.Exists(r => r.Rule == "timestamp" && r.Valid)
        ));
    }

    [Test]
    public void TracingHeaderValidator_should_validate_base_fee()
    {
        // Arrange
        var tracer = Substitute.For<IBlockTracer>();
        var validator = new TracingHeaderValidator(tracer);

        var parent = Build.A.BlockHeader
            .WithGasLimit(30000000)
            .WithGasUsed(15000000)
            .WithBaseFee(1000000000)
            .TestObject;

        var header = Build.A.BlockHeader
            .WithGasLimit(30000000)
            .WithBaseFee(1000000000) // Same as parent (gas used = target)
            .TestObject;

        // Act
        validator.TraceHeaderValidation(header, parent, isValid: true);

        // Assert
        tracer.Received(1).TraceValidation(Arg.Is<HeaderValidationOperation>(op =>
            op.Rules.Exists(r => r.Rule == "baseFee")
        ));
    }

    [Test]
    public void TracingHeaderValidator_should_validate_excess_blob_gas()
    {
        // Arrange
        var tracer = Substitute.For<IBlockTracer>();
        var validator = new TracingHeaderValidator(tracer);

        var parent = Build.A.BlockHeader
            .WithExcessBlobGas(100000)
            .WithBlobGasUsed(393216) // Target blob gas per block
            .TestObject;

        var header = Build.A.BlockHeader
            .WithExcessBlobGas(100000) // Same as parent (used = target)
            .TestObject;

        // Act
        validator.TraceHeaderValidation(header, parent, isValid: true);

        // Assert
        tracer.Received(1).TraceValidation(Arg.Is<HeaderValidationOperation>(op =>
            op.Rules.Exists(r => r.Rule == "excessBlobGas")
        ));
    }

    [Test]
    public void TracingHeaderValidator_should_handle_null_headers()
    {
        // Arrange
        var tracer = Substitute.For<IBlockTracer>();
        var validator = new TracingHeaderValidator(tracer);

        // Act
        validator.TraceHeaderValidation(null, null, isValid: false);

        // Assert - Should not call tracer
        tracer.DidNotReceive().TraceValidation(Arg.Any<ValidationOperation>());
    }

    #endregion

    #region TracingGasValidator Tests

    [Test]
    public void TracingGasValidator_should_throw_when_tracer_is_null()
    {
        // Act & Assert
        Action act = () => new TracingGasValidator(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void TracingGasValidator_should_trace_gas_accounting()
    {
        // Arrange
        var tracer = Substitute.For<IBlockTracer>();
        var validator = new TracingGasValidator(tracer);

        var transactions = new[]
        {
            Build.A.Transaction.WithGasLimit(21000).TestObject,
            Build.A.Transaction.WithGasLimit(50000).TestObject
        };

        var gasUsed = new long[] { 21000, 45000 };

        // Act
        validator.TraceGasAccounting(transactions, gasUsed, blockGasLimit: 30000000, isValid: true);

        // Assert
        tracer.Received(1).TraceValidation(Arg.Is<GasAccountingOperation>(op =>
            op.Operation == "gasAccounting" &&
            op.Transactions.Count == 2 &&
            op.TotalGasUsed == "0x101d0" && // 66000 in hex
            op.Valid
        ));
    }

    [Test]
    public void TracingGasValidator_should_calculate_cumulative_gas()
    {
        // Arrange
        var tracer = Substitute.For<IBlockTracer>();
        var validator = new TracingGasValidator(tracer);

        var transactions = new[]
        {
            Build.A.Transaction.TestObject,
            Build.A.Transaction.TestObject,
            Build.A.Transaction.TestObject
        };

        var gasUsed = new long[] { 21000, 30000, 40000 };

        // Act
        validator.TraceGasAccounting(transactions, gasUsed, blockGasLimit: 30000000, isValid: true);

        // Assert
        tracer.Received(1).TraceValidation(Arg.Is<GasAccountingOperation>(op =>
            op.Transactions[0].CumulativeGasUsed == "0x5208" && // 21000
            op.Transactions[1].CumulativeGasUsed == "0xc738" && // 51000
            op.Transactions[2].CumulativeGasUsed == "0x163a8" // 91000
        ));
    }

    [Test]
    public void TracingGasValidator_should_trace_blob_gas_accounting()
    {
        // Arrange
        var tracer = Substitute.For<IBlockTracer>();
        var validator = new TracingGasValidator(tracer);

        var blobTx = Build.A.Transaction
            .WithType(TxType.Blob)
            .WithBlobVersionedHashes(2) // 2 blobs
            .TestObject;

        // Act
        validator.TraceBlobGasAccounting(
            new[] { blobTx },
            excessBlobGas: 100000,
            blobGasPrice: 1000,
            isValid: true);

        // Assert
        tracer.Received(1).TraceValidation(Arg.Is<BlobGasAccountingOperation>(op =>
            op.Operation == "blobGasAccounting" &&
            op.Transactions[0].BlobCount == "0x2" &&
            op.Transactions[0].BlobGasPerBlob == "0x20000" && // 131072
            op.BlobGasPriceCalculation != null
        ));
    }

    [Test]
    public void TracingGasValidator_should_calculate_blob_gas_price()
    {
        // Arrange
        var tracer = Substitute.For<IBlockTracer>();
        var validator = new TracingGasValidator(tracer);

        ulong excessBlobGas = 500000;

        // Act
        validator.TraceBlobGasAccounting(
            Array.Empty<Transaction>(),
            excessBlobGas,
            blobGasPrice: 1234,
            isValid: true);

        // Assert
        tracer.Received(1).TraceValidation(Arg.Is<BlobGasAccountingOperation>(op =>
            op.BlobGasPriceCalculation!.ExcessBlobGas == $"0x{excessBlobGas:x}" &&
            op.BlobGasPriceCalculation.FakeExponential != null
        ));
    }

    [Test]
    public void TracingGasValidator_should_handle_null_transactions()
    {
        // Arrange
        var tracer = Substitute.For<IBlockTracer>();
        var validator = new TracingGasValidator(tracer);

        // Act
        validator.TraceGasAccounting(null, null, blockGasLimit: 30000000, isValid: true);

        // Assert - Should not call tracer
        tracer.DidNotReceive().TraceValidation(Arg.Any<ValidationOperation>());
    }

    #endregion
}
