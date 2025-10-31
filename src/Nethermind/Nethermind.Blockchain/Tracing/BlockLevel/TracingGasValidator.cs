// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using System.Linq;
using Nethermind.Core;
using Nethermind.Core.Extensions;
using Nethermind.Evm;
using Nethermind.Evm.Tracing;
using Nethermind.Evm.Tracing.BlockOperations;
using Nethermind.Int256;

namespace Nethermind.Blockchain.Tracing.BlockLevel;

/// <summary>
/// Wraps gas accounting validation to emit validation trace records.
/// This captures both regular gas accounting (transaction gas) and blob gas accounting (EIP-4844)
/// including the fake exponential calculation for blob gas pricing.
/// </summary>
public class TracingGasValidator
{
    private readonly IBlockTracer _tracer;

    // EIP-4844 constants
    private const long GasPerBlob = 131072; // 2^17
    private const long MaxBlobGasPerBlock = 786432; // 6 * 2^17
    private const long MinBlobGasPrice = 1;
    private const long BlobGasPriceFactor = 1; // MIN_BASE_FEE_PER_BLOB_GAS

    /// <summary>
    /// Initializes a new instance of the <see cref="TracingGasValidator"/> class.
    /// </summary>
    /// <param name="tracer">The block tracer to emit trace records to.</param>
    public TracingGasValidator(IBlockTracer tracer)
    {
        _tracer = tracer ?? throw new ArgumentNullException(nameof(tracer));
    }

    /// <summary>
    /// Traces gas accounting for all transactions in a block.
    /// This validates that cumulative gas usage doesn't exceed the block gas limit.
    /// </summary>
    /// <param name="transactions">Transactions in the block.</param>
    /// <param name="gasUsed">Gas used by each transaction.</param>
    /// <param name="blockGasLimit">Block gas limit.</param>
    /// <param name="isValid">Whether gas accounting is valid.</param>
    public void TraceGasAccounting(
        Transaction[]? transactions,
        long[]? gasUsed,
        long blockGasLimit,
        bool isValid)
    {
        if (transactions is null || transactions.Length == 0)
        {
            return;
        }

        var operation = new GasAccountingOperation
        {
            BlockGasLimit = $"0x{blockGasLimit:x}",
            Valid = isValid,
            OverallResult = isValid ? "valid" : "invalid"
        };

        long cumulativeGas = 0;

        for (int i = 0; i < transactions.Length; i++)
        {
            var tx = transactions[i];
            long txGasUsed = gasUsed is not null && i < gasUsed.Length ? gasUsed[i] : 0;
            cumulativeGas += txGasUsed;

            operation.Transactions.Add(new TransactionGasAccounting
            {
                TxIndex = $"0x{i:x}",
                GasLimit = $"0x{tx.GasLimit:x}",
                GasUsed = $"0x{txGasUsed:x}",
                CumulativeGasUsed = $"0x{cumulativeGas:x}"
            });
        }

        operation.TotalGasUsed = $"0x{cumulativeGas:x}";
        operation.GasLimitExceeded = cumulativeGas > blockGasLimit;

        _tracer.TraceValidation(operation);
    }

    /// <summary>
    /// Traces blob gas accounting for blob-carrying transactions (EIP-4844).
    /// This includes the fake exponential calculation for blob gas pricing.
    /// </summary>
    /// <param name="blobTxs">Blob-carrying transactions (type 3).</param>
    /// <param name="excessBlobGas">Excess blob gas from parent block.</param>
    /// <param name="blobGasPrice">Calculated blob gas price.</param>
    /// <param name="isValid">Whether blob gas accounting is valid.</param>
    /// <param name="maxBlobGasPerBlock">Maximum blob gas per block (fork-specific).</param>
    /// <param name="originalIndices">Original transaction indices in the block (if different from array order).</param>
    /// <param name="blobGasPriceUpdateFraction">Blob gas price update fraction (fork-specific).</param>
    public void TraceBlobGasAccounting(
        Transaction[]? blobTxs,
        ulong excessBlobGas,
        UInt256 blobGasPrice,
        bool isValid,
        long? maxBlobGasPerBlock = null,
        int[]? originalIndices = null,
        UInt256? blobGasPriceUpdateFraction = null)
    {
        long maxBlobGas = maxBlobGasPerBlock ?? MaxBlobGasPerBlock;
        var operation = new BlobGasAccountingOperation
        {
            MaxBlobGasPerBlock = $"0x{maxBlobGas:x}",
            Valid = isValid,
            OverallResult = isValid ? "valid" : "invalid"
        };

        // Calculate blob gas price using fake exponential
        UInt256 updateFraction = blobGasPriceUpdateFraction ?? Eip4844Constants.DefaultBlobGasPriceUpdateFraction;
        var fakeExp = CalculateFakeExponential(excessBlobGas, updateFraction);

        operation.BlobGasPriceCalculation = new BlobGasPriceCalculation
        {
            ExcessBlobGas = $"0x{excessBlobGas:x}",
            FakeExponential = fakeExp,
            BlobGasPrice = $"0x{blobGasPrice:x}",
            MinBlobGasPrice = $"0x{MinBlobGasPrice:x}"
        };

        // Process blob transactions
        long cumulativeBlobGas = 0;

        if (blobTxs is not null)
        {
            for (int i = 0; i < blobTxs.Length; i++)
            {
                var tx = blobTxs[i];
                int txIndex = originalIndices is not null && i < originalIndices.Length ? originalIndices[i] : i;

                // Get blob count from versioned hashes
                int blobCount = tx.BlobVersionedHashes?.Length ?? 0;
                long txBlobGas = blobCount * GasPerBlob;
                cumulativeBlobGas += txBlobGas;

                operation.Transactions.Add(new TransactionBlobGasAccounting
                {
                    TxIndex = $"0x{txIndex:x}",
                    BlobCount = $"0x{blobCount:x}",
                    BlobGasPerBlob = $"0x{GasPerBlob:x}",
                    BlobGasUsed = $"0x{txBlobGas:x}",
                    CumulativeBlobGasUsed = $"0x{cumulativeBlobGas:x}"
                });
            }
        }

        operation.TotalBlobGasUsed = $"0x{cumulativeBlobGas:x}";
        operation.BlobGasLimitExceeded = cumulativeBlobGas > maxBlobGas;

        _tracer.TraceValidation(operation);
    }

    /// <summary>
    /// Calculates the fake exponential for blob gas price.
    /// This is an integer approximation of: factor * e^(excess_blob_gas / BLOB_GAS_UPDATE_FRACTION)
    /// </summary>
    private FakeExponential CalculateFakeExponential(ulong excessBlobGas, UInt256 updateFraction)
    {
        var fakeExp = new FakeExponential
        {
            Factor = $"0x{BlobGasPriceFactor:x}",
            Numerator = $"0x{excessBlobGas:x}",
            Denominator = updateFraction.ToHexString(true)
        };

        // Calculate using integer arithmetic (Taylor expansion)
        // output = factor * e^(numerator / denominator)
        // Using Taylor series: sum from i=0 of (factor * numerator^i) / (denominator^i * i!)

        UInt256 factor = (UInt256)BlobGasPriceFactor;
        UInt256 numerator = (UInt256)excessBlobGas;
        UInt256 denominator = updateFraction;

        // Initialize accumulator = factor * denominator
        UInt256 accumulator;
        if (factor == UInt256.One)
        {
            // Skip expensive 256bit multiplication if factor is 1
            accumulator = denominator;
        }
        else
        {
            accumulator = factor * denominator;
        }

        UInt256 output = UInt256.Zero;

        // Iterate until accumulator becomes zero (convergence)
        for (ulong i = 1; !accumulator.IsZero; i++)
        {
            // Add current term to output
            output += accumulator;

            // Record first iteration for tracing (matches geth format)
            if (i == 1)
            {
                fakeExp.Iterations.Add(new FakeExponentialIteration
                {
                    I = "0x0",
                    Accumulator = accumulator.ToHexString(true),
                    Overflow = false
                });
            }

            // Calculate next term: accumulator = (accumulator * numerator) / (denominator * i)
            accumulator = accumulator * numerator / (denominator * i);
        }

        // Divide by denominator to get final result
        UInt256 result = output / denominator;
        fakeExp.Result = result.ToHexString(true);

        return fakeExp;
    }

    /// <summary>
    /// Traces gas accounting with cumulative tracking.
    /// This overload accepts a list of (transaction, gasUsed) tuples.
    /// </summary>
    /// <param name="transactionGasData">List of (transaction, gasUsed) tuples.</param>
    /// <param name="blockGasLimit">Block gas limit.</param>
    /// <param name="isValid">Whether gas accounting is valid.</param>
    public void TraceGasAccounting(
        IEnumerable<(Transaction tx, long gasUsed)>? transactionGasData,
        long blockGasLimit,
        bool isValid)
    {
        if (transactionGasData is null)
        {
            return;
        }

        var data = transactionGasData.ToArray();
        var transactions = data.Select(x => x.tx).ToArray();
        var gasUsed = data.Select(x => x.gasUsed).ToArray();

        TraceGasAccounting(transactions, gasUsed, blockGasLimit, isValid);
    }

    /// <summary>
    /// Traces blob gas accounting with detailed transaction information.
    /// This overload accepts blob transaction details with their blob counts.
    /// </summary>
    /// <param name="blobTransactionData">List of (transaction, blobCount, blobGasUsed) tuples.</param>
    /// <param name="excessBlobGas">Excess blob gas from parent block.</param>
    /// <param name="isValid">Whether blob gas accounting is valid.</param>
    /// <param name="maxBlobGasPerBlock">Maximum blob gas per block (fork-specific).</param>
    /// <param name="blobGasPriceUpdateFraction">Blob gas price update fraction (fork-specific).</param>
    public void TraceBlobGasAccounting(
        IEnumerable<(Transaction tx, int blobCount, long blobGasUsed)>? blobTransactionData,
        ulong excessBlobGas,
        bool isValid,
        long? maxBlobGasPerBlock = null,
        UInt256? blobGasPriceUpdateFraction = null)
    {
        long maxBlobGas = maxBlobGasPerBlock ?? MaxBlobGasPerBlock;
        var operation = new BlobGasAccountingOperation
        {
            MaxBlobGasPerBlock = $"0x{maxBlobGas:x}",
            Valid = isValid,
            OverallResult = isValid ? "valid" : "invalid"
        };

        // Calculate blob gas price
        UInt256 updateFraction = blobGasPriceUpdateFraction ?? Eip4844Constants.DefaultBlobGasPriceUpdateFraction;
        BlobGasCalculator.TryCalculateFeePerBlobGas(excessBlobGas, updateFraction, out UInt256 blobGasPrice);
        var fakeExp = CalculateFakeExponential(excessBlobGas, updateFraction);

        operation.BlobGasPriceCalculation = new BlobGasPriceCalculation
        {
            ExcessBlobGas = $"0x{excessBlobGas:x}",
            FakeExponential = fakeExp,
            BlobGasPrice = $"0x{blobGasPrice:x}",
            MinBlobGasPrice = $"0x{MinBlobGasPrice:x}"
        };

        long cumulativeBlobGas = 0;

        if (blobTransactionData is not null)
        {
            int txIndex = 0;
            foreach (var (tx, blobCount, blobGasUsed) in blobTransactionData)
            {
                cumulativeBlobGas += blobGasUsed;

                operation.Transactions.Add(new TransactionBlobGasAccounting
                {
                    TxIndex = $"0x{txIndex:x}",
                    BlobCount = $"0x{blobCount:x}",
                    BlobGasPerBlob = $"0x{GasPerBlob:x}",
                    BlobGasUsed = $"0x{blobGasUsed:x}",
                    CumulativeBlobGasUsed = $"0x{cumulativeBlobGas:x}"
                });

                txIndex++;
            }
        }

        operation.TotalBlobGasUsed = $"0x{cumulativeBlobGas:x}";
        operation.BlobGasLimitExceeded = cumulativeBlobGas > maxBlobGas;

        _tracer.TraceValidation(operation);
    }
}
