// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Collections.Generic;
using System.Text.Json.Serialization;
using Nethermind.Core;
using Nethermind.Int256;

namespace Nethermind.Evm.Tracing.BlockOperations;

/// <summary>
/// Base class for validation operations that verify block correctness
/// </summary>
public abstract class ValidationOperation
{
    /// <summary>
    /// Type of validation operation (e.g., "headerValidation", "gasAccounting", "blobGasAccounting")
    /// </summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>
    /// Whether the validation passed
    /// </summary>
    public bool Valid { get; set; }

    /// <summary>
    /// Overall validation result (e.g., "valid", "invalid")
    /// </summary>
    public string OverallResult { get; set; } = string.Empty;
}

/// <summary>
/// Header validation operation that checks block header fields against consensus rules
/// </summary>
public class HeaderValidationOperation : ValidationOperation
{
    /// <summary>
    /// List of validation rules checked
    /// </summary>
    public List<ValidationRule> Rules { get; set; } = new();

    public HeaderValidationOperation()
    {
        Operation = "headerValidation";
    }
}

/// <summary>
/// A single validation rule with its parameters and result
/// </summary>
public class ValidationRule
{
    /// <summary>
    /// Name of the validation rule (e.g., "gasLimit", "timestamp", "baseFee", "excessBlobGas")
    /// </summary>
    public string Rule { get; set; } = string.Empty;

    /// <summary>
    /// Whether this rule passed validation
    /// </summary>
    public bool Valid { get; set; }

    /// <summary>
    /// Parent block's value for comparison (hexadecimal)
    /// </summary>
    public string? ParentValue { get; set; }

    /// <summary>
    /// Current block's value (hexadecimal)
    /// </summary>
    public string? CurrentValue { get; set; }

    /// <summary>
    /// Delta between parent and current (hexadecimal)
    /// </summary>
    public string? Delta { get; set; }

    /// <summary>
    /// Maximum allowed delta (hexadecimal)
    /// </summary>
    public string? MaxDelta { get; set; }

    /// <summary>
    /// Actual value from the block header (hexadecimal)
    /// </summary>
    public string? ActualValue { get; set; }

    /// <summary>
    /// Expected value according to consensus rules (hexadecimal)
    /// </summary>
    public string? ExpectedValue { get; set; }

    /// <summary>
    /// Detailed calculation information for complex rules
    /// </summary>
    public RuleCalculation? Calculation { get; set; }
}

/// <summary>
/// Calculation details for validation rules (e.g., base fee calculation)
/// </summary>
public class RuleCalculation
{
    /// <summary>
    /// Parent block's gas used (hexadecimal)
    /// </summary>
    public string? ParentGasUsed { get; set; }

    /// <summary>
    /// Parent block's gas target (hexadecimal)
    /// </summary>
    public string? ParentGasTarget { get; set; }

    /// <summary>
    /// Parent block's base fee (hexadecimal)
    /// </summary>
    public string? ParentBaseFee { get; set; }

    /// <summary>
    /// Gas used delta from target (hexadecimal)
    /// </summary>
    public string? GasUsedDelta { get; set; }

    /// <summary>
    /// Base fee delta calculated (hexadecimal)
    /// </summary>
    public string? BaseFeeDelta { get; set; }

    /// <summary>
    /// Expected base fee after calculation (hexadecimal)
    /// </summary>
    public string? ExpectedBaseFee { get; set; }

    /// <summary>
    /// Parent block's excess blob gas (hexadecimal)
    /// </summary>
    public string? ParentExcessBlobGas { get; set; }

    /// <summary>
    /// Parent block's blob gas used (hexadecimal)
    /// </summary>
    public string? ParentBlobGasUsed { get; set; }

    /// <summary>
    /// Target blob gas per block (hexadecimal)
    /// </summary>
    public string? TargetBlobGasPerBlock { get; set; }

    /// <summary>
    /// Blob gas used delta from target (hexadecimal)
    /// </summary>
    public string? BlobGasUsedDelta { get; set; }

    /// <summary>
    /// Expected excess blob gas after calculation (hexadecimal)
    /// </summary>
    public string? ExpectedExcessBlobGas { get; set; }

    /// <summary>
    /// Formula used for this calculation
    /// </summary>
    public string? Formula { get; set; }
}

/// <summary>
/// Gas accounting validation that ensures transactions don't exceed block gas limit
/// </summary>
public class GasAccountingOperation : ValidationOperation
{
    /// <summary>
    /// Gas accounting for each transaction (null when block has no transactions)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public List<TransactionGasAccounting>? Transactions { get; set; } = new();

    /// <summary>
    /// Block gas limit (hexadecimal)
    /// </summary>
    public string BlockGasLimit { get; set; } = string.Empty;

    /// <summary>
    /// Total gas used by all transactions (hexadecimal)
    /// </summary>
    public string TotalGasUsed { get; set; } = "0x0";

    /// <summary>
    /// Whether the block gas limit was exceeded
    /// </summary>
    public bool GasLimitExceeded { get; set; }

    public GasAccountingOperation()
    {
        Operation = "gasAccounting";
    }
}

/// <summary>
/// Gas accounting information for a single transaction
/// </summary>
public class TransactionGasAccounting
{
    /// <summary>
    /// Transaction index in block (hexadecimal)
    /// </summary>
    public string TxIndex { get; set; } = string.Empty;

    /// <summary>
    /// Transaction gas limit (hexadecimal)
    /// </summary>
    public string GasLimit { get; set; } = string.Empty;

    /// <summary>
    /// Actual gas used by transaction (hexadecimal)
    /// </summary>
    public string GasUsed { get; set; } = string.Empty;

    /// <summary>
    /// Cumulative gas used up to and including this transaction (hexadecimal)
    /// </summary>
    public string CumulativeGasUsed { get; set; } = string.Empty;
}

/// <summary>
/// Blob gas accounting validation (EIP-4844) for blob-carrying transactions
/// </summary>
public class BlobGasAccountingOperation : ValidationOperation
{
    /// <summary>
    /// Blob gas price calculation details
    /// </summary>
    public BlobGasPriceCalculation BlobGasPriceCalculation { get; set; } = new();

    /// <summary>
    /// Blob gas accounting for each transaction
    /// </summary>
    public List<TransactionBlobGasAccounting> Transactions { get; set; } = new();

    /// <summary>
    /// Maximum blob gas per block (hexadecimal)
    /// </summary>
    public string MaxBlobGasPerBlock { get; set; } = string.Empty;

    /// <summary>
    /// Total blob gas used by all transactions (hexadecimal)
    /// </summary>
    public string TotalBlobGasUsed { get; set; } = "0x0";

    /// <summary>
    /// Whether the blob gas limit was exceeded
    /// </summary>
    public bool BlobGasLimitExceeded { get; set; }

    public BlobGasAccountingOperation()
    {
        Operation = "blobGasAccounting";
    }
}

/// <summary>
/// Blob gas price calculation using fake exponential function
/// </summary>
public class BlobGasPriceCalculation
{
    /// <summary>
    /// Excess blob gas used in calculation (hexadecimal)
    /// </summary>
    public string ExcessBlobGas { get; set; } = string.Empty;

    /// <summary>
    /// Fake exponential calculation details
    /// </summary>
    public FakeExponential FakeExponential { get; set; } = new();

    /// <summary>
    /// Calculated blob gas price (hexadecimal)
    /// </summary>
    public string BlobGasPrice { get; set; } = string.Empty;

    /// <summary>
    /// Minimum blob gas price (hexadecimal)
    /// </summary>
    public string MinBlobGasPrice { get; set; } = "0x1";
}

/// <summary>
/// Fake exponential calculation using integer arithmetic
/// </summary>
public class FakeExponential
{
    /// <summary>
    /// Factor used in calculation (hexadecimal)
    /// </summary>
    public string Factor { get; set; } = string.Empty;

    /// <summary>
    /// Numerator of the exponential (hexadecimal)
    /// </summary>
    public string Numerator { get; set; } = string.Empty;

    /// <summary>
    /// Denominator of the exponential (hexadecimal)
    /// </summary>
    public string Denominator { get; set; } = string.Empty;

    /// <summary>
    /// Iteration-by-iteration calculation showing no overflow
    /// </summary>
    public List<FakeExponentialIteration> Iterations { get; set; } = new();

    /// <summary>
    /// Final result of the calculation (hexadecimal)
    /// </summary>
    public string Result { get; set; } = string.Empty;
}

/// <summary>
/// A single iteration in the fake exponential calculation
/// </summary>
public class FakeExponentialIteration
{
    /// <summary>
    /// Iteration number (hexadecimal)
    /// </summary>
    public string I { get; set; } = string.Empty;

    /// <summary>
    /// Accumulator value at this iteration (hexadecimal)
    /// </summary>
    public string Accumulator { get; set; } = string.Empty;

    /// <summary>
    /// Whether overflow occurred in this iteration
    /// </summary>
    public bool Overflow { get; set; }
}

/// <summary>
/// Blob gas accounting for a single transaction
/// </summary>
public class TransactionBlobGasAccounting
{
    /// <summary>
    /// Transaction index in block (hexadecimal)
    /// </summary>
    public string TxIndex { get; set; } = string.Empty;

    /// <summary>
    /// Number of blobs in this transaction (hexadecimal)
    /// </summary>
    public string BlobCount { get; set; } = string.Empty;

    /// <summary>
    /// Gas per blob (131072 = 2^17) (hexadecimal)
    /// </summary>
    public string BlobGasPerBlob { get; set; } = "0x20000";

    /// <summary>
    /// Total blob gas used by this transaction (hexadecimal)
    /// </summary>
    public string BlobGasUsed { get; set; } = string.Empty;

    /// <summary>
    /// Cumulative blob gas used up to and including this transaction (hexadecimal)
    /// </summary>
    public string CumulativeBlobGasUsed { get; set; } = string.Empty;
}
