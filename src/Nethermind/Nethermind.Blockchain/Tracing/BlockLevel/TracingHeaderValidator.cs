// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using Nethermind.Core;
using Nethermind.Evm.Tracing;
using Nethermind.Evm.Tracing.BlockOperations;
using Nethermind.Int256;

namespace Nethermind.Blockchain.Tracing.BlockLevel;

/// <summary>
/// Wraps header validation to emit validation trace records.
/// This captures consensus-critical validation operations including gas limit checks,
/// timestamp validation, base fee calculation (EIP-1559), and excess blob gas calculation (EIP-4844).
/// </summary>
public class TracingHeaderValidator
{
    private readonly IBlockTracer _tracer;

    // EIP-1559 constants
    private const long ElasticityMultiplier = 2;
    private const long BaseFeeMaxChangeDenominator = 8;

    // EIP-4844 constants
    private const long GasPerBlob = 131072; // 2^17
    private const long TargetBlobGasPerBlock = 393216; // 3 * 2^17

    /// <summary>
    /// Initializes a new instance of the <see cref="TracingHeaderValidator"/> class.
    /// </summary>
    /// <param name="tracer">The block tracer to emit trace records to.</param>
    public TracingHeaderValidator(IBlockTracer tracer)
    {
        _tracer = tracer ?? throw new ArgumentNullException(nameof(tracer));
    }

    /// <summary>
    /// Traces header validation showing all validation rules checked and their results.
    /// </summary>
    /// <param name="header">Current block header being validated.</param>
    /// <param name="parent">Parent block header for comparison.</param>
    /// <param name="isValid">Whether the header passed all validation checks.</param>
    public void TraceHeaderValidation(
        BlockHeader? header,
        BlockHeader? parent,
        bool isValid)
    {
        if (header is null || parent is null)
        {
            return;
        }

        var operation = new HeaderValidationOperation
        {
            Valid = isValid,
            OverallResult = isValid ? "valid" : "invalid"
        };

        // Gas limit validation
        operation.Rules.Add(ValidateGasLimit(header, parent));

        // Timestamp validation
        operation.Rules.Add(ValidateTimestamp(header, parent));

        // Base fee validation (EIP-1559)
        if (header.BaseFeePerGas > 0 || parent.BaseFeePerGas > 0)
        {
            operation.Rules.Add(ValidateBaseFee(header, parent));
        }

        // Excess blob gas validation (EIP-4844)
        if (header.ExcessBlobGas.HasValue)
        {
            operation.Rules.Add(ValidateExcessBlobGas(header, parent));
        }

        _tracer.TraceValidation(operation);
    }

    /// <summary>
    /// Validates gas limit is within allowed delta from parent.
    /// </summary>
    private ValidationRule ValidateGasLimit(BlockHeader header, BlockHeader parent)
    {
        long parentGasLimit = parent.GasLimit;
        long currentGasLimit = header.GasLimit;
        long delta = Math.Abs(currentGasLimit - parentGasLimit);
        long maxDelta = parentGasLimit / 1024; // Max delta is parent / 1024

        bool valid = delta <= maxDelta;

        return new ValidationRule
        {
            Rule = "gasLimit",
            ParentValue = $"0x{parentGasLimit:x}",
            CurrentValue = $"0x{currentGasLimit:x}",
            Delta = $"0x{delta:x}",
            MaxDelta = $"0x{maxDelta:x}",
            Valid = valid
        };
    }

    /// <summary>
    /// Validates timestamp is greater than parent timestamp.
    /// </summary>
    private ValidationRule ValidateTimestamp(BlockHeader header, BlockHeader parent)
    {
        ulong parentTimestamp = parent.Timestamp;
        ulong currentTimestamp = header.Timestamp;
        long delta = (long)(currentTimestamp - parentTimestamp);

        bool valid = currentTimestamp > parentTimestamp;

        return new ValidationRule
        {
            Rule = "timestamp",
            ParentValue = $"0x{parentTimestamp:x}",
            CurrentValue = $"0x{currentTimestamp:x}",
            Delta = $"0x{delta:x}",
            Valid = valid
        };
    }

    /// <summary>
    /// Validates base fee per gas calculation (EIP-1559).
    /// Formula: parentBaseFee + parentBaseFee * gasUsedDelta / gasTarget / 8
    /// </summary>
    private ValidationRule ValidateBaseFee(BlockHeader header, BlockHeader parent)
    {
        if (parent.BaseFeePerGas == 0)
        {
            // First EIP-1559 block - base fee should be set to initial value
            return new ValidationRule
            {
                Rule = "baseFee",
                ActualValue = $"0x{header.BaseFeePerGas:x}",
                Valid = true
            };
        }

        long parentGasUsed = parent.GasUsed;
        long parentGasTarget = parent.GasLimit / ElasticityMultiplier;
        UInt256 parentBaseFee = parent.BaseFeePerGas;

        long gasUsedDelta = parentGasUsed - parentGasTarget;
        UInt256 expectedBaseFee;

        if (gasUsedDelta == 0)
        {
            // No change
            expectedBaseFee = parentBaseFee;
        }
        else if (gasUsedDelta > 0)
        {
            // Increase base fee
            UInt256 baseFeePerGasDelta = UInt256.Max(
                parentBaseFee * (UInt256)gasUsedDelta / (UInt256)parentGasTarget / BaseFeeMaxChangeDenominator,
                1);
            expectedBaseFee = parentBaseFee + baseFeePerGasDelta;
        }
        else
        {
            // Decrease base fee
            UInt256 baseFeePerGasDelta =
                parentBaseFee * (UInt256)(-gasUsedDelta) / (UInt256)parentGasTarget / BaseFeeMaxChangeDenominator;
            expectedBaseFee = UInt256.Max(parentBaseFee - baseFeePerGasDelta, 0);
        }

        UInt256 actualBaseFee = header.BaseFeePerGas;
        bool valid = actualBaseFee == expectedBaseFee;

        var calculation = new RuleCalculation
        {
            ParentGasUsed = $"0x{parentGasUsed:x}",
            ParentGasTarget = $"0x{parentGasTarget:x}",
            ParentBaseFee = $"0x{parentBaseFee:x}",
            GasUsedDelta = $"0x{gasUsedDelta:x}",
            ExpectedBaseFee = $"0x{expectedBaseFee:x}",
            Formula = "parentBaseFee + parentBaseFee * gasUsedDelta / gasTarget / 8"
        };

        return new ValidationRule
        {
            Rule = "baseFee",
            ExpectedValue = $"0x{expectedBaseFee:x}",
            ActualValue = $"0x{actualBaseFee:x}",
            Calculation = calculation,
            Valid = valid
        };
    }

    /// <summary>
    /// Validates excess blob gas calculation (EIP-4844).
    /// Formula: max(parentExcessBlobGas + parentBlobGasUsed - targetBlobGasPerBlock, 0)
    /// </summary>
    private ValidationRule ValidateExcessBlobGas(BlockHeader header, BlockHeader parent)
    {
        if (!parent.ExcessBlobGas.HasValue)
        {
            // First EIP-4844 block - excess blob gas should be 0
            return new ValidationRule
            {
                Rule = "excessBlobGas",
                ActualValue = $"0x{header.ExcessBlobGas.GetValueOrDefault():x}",
                ExpectedValue = "0x0",
                Valid = header.ExcessBlobGas.GetValueOrDefault() == 0
            };
        }

        ulong parentExcessBlobGas = parent.ExcessBlobGas.GetValueOrDefault();
        ulong parentBlobGasUsed = parent.BlobGasUsed.GetValueOrDefault();

        // Calculate expected excess blob gas
        long tempExcess = (long)parentExcessBlobGas + (long)parentBlobGasUsed - TargetBlobGasPerBlock;
        ulong expectedExcessBlobGas = tempExcess > 0 ? (ulong)tempExcess : 0;

        ulong actualExcessBlobGas = header.ExcessBlobGas.GetValueOrDefault();
        bool valid = actualExcessBlobGas == expectedExcessBlobGas;

        long blobGasUsedDelta = (long)parentBlobGasUsed - TargetBlobGasPerBlock;

        var calculation = new RuleCalculation
        {
            ParentExcessBlobGas = $"0x{parentExcessBlobGas:x}",
            ParentBlobGasUsed = $"0x{parentBlobGasUsed:x}",
            TargetBlobGasPerBlock = $"0x{TargetBlobGasPerBlock:x}",
            BlobGasUsedDelta = $"0x{blobGasUsedDelta:x}",
            ExpectedExcessBlobGas = $"0x{expectedExcessBlobGas:x}",
            Formula = "max(parentExcessBlobGas + parentBlobGasUsed - targetBlobGasPerBlock, 0)"
        };

        return new ValidationRule
        {
            Rule = "excessBlobGas",
            ExpectedValue = $"0x{expectedExcessBlobGas:x}",
            ActualValue = $"0x{actualExcessBlobGas:x}",
            Calculation = calculation,
            Valid = valid
        };
    }

    /// <summary>
    /// Traces header validation with individual rule results.
    /// This overload allows specifying which rules to validate.
    /// </summary>
    /// <param name="header">Current block header.</param>
    /// <param name="parent">Parent block header.</param>
    /// <param name="validateGasLimit">Whether to validate gas limit.</param>
    /// <param name="validateTimestamp">Whether to validate timestamp.</param>
    /// <param name="validateBaseFee">Whether to validate base fee.</param>
    /// <param name="validateExcessBlobGas">Whether to validate excess blob gas.</param>
    public void TraceHeaderValidation(
        BlockHeader? header,
        BlockHeader? parent,
        bool validateGasLimit = true,
        bool validateTimestamp = true,
        bool validateBaseFee = true,
        bool validateExcessBlobGas = true)
    {
        if (header is null || parent is null)
        {
            return;
        }

        var operation = new HeaderValidationOperation();

        bool allValid = true;

        if (validateGasLimit)
        {
            var rule = ValidateGasLimit(header, parent);
            operation.Rules.Add(rule);
            allValid &= rule.Valid;
        }

        if (validateTimestamp)
        {
            var rule = ValidateTimestamp(header, parent);
            operation.Rules.Add(rule);
            allValid &= rule.Valid;
        }

        if (validateBaseFee && (header.BaseFeePerGas > 0 || parent.BaseFeePerGas > 0))
        {
            var rule = ValidateBaseFee(header, parent);
            operation.Rules.Add(rule);
            allValid &= rule.Valid;
        }

        if (validateExcessBlobGas && header.ExcessBlobGas.HasValue)
        {
            var rule = ValidateExcessBlobGas(header, parent);
            operation.Rules.Add(rule);
            allValid &= rule.Valid;
        }

        operation.Valid = allValid;
        operation.OverallResult = allValid ? "valid" : "invalid";

        _tracer.TraceValidation(operation);
    }
}
