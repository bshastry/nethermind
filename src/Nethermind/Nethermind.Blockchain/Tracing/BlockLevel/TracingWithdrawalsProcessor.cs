// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using Nethermind.Core;
using Nethermind.Evm.Tracing;
using Nethermind.Evm.Tracing.BlockOperations;
using Nethermind.Int256;

namespace Nethermind.Blockchain.Tracing.BlockLevel;

/// <summary>
/// Wraps withdrawal processing to emit post-execution trace records.
/// This captures validator withdrawals from the beacon chain (EIP-4895) including
/// balance changes, Gwei to Wei conversion, and account creation/deletion.
/// </summary>
public class TracingWithdrawalsProcessor
{
    private readonly IBlockTracer _tracer;

    /// <summary>
    /// Initializes a new instance of the <see cref="TracingWithdrawalsProcessor"/> class.
    /// </summary>
    /// <param name="tracer">The block tracer to emit trace records to.</param>
    public TracingWithdrawalsProcessor(IBlockTracer tracer)
    {
        _tracer = tracer ?? throw new ArgumentNullException(nameof(tracer));
    }

    /// <summary>
    /// Traces withdrawal operations showing the full details of each withdrawal including
    /// Gwei to Wei conversion (multiply by 10^9) and resulting balance changes.
    /// </summary>
    /// <param name="withdrawals">Array of withdrawals to process.</param>
    /// <param name="balanceChanges">Dictionary mapping addresses to (before, after) balance tuples.</param>
    /// <param name="accountsCreated">Number of new accounts created for withdrawals.</param>
    /// <param name="emptyAccountsDeleted">Number of empty accounts deleted per EIP-161.</param>
    public void TraceWithdrawals(
        Withdrawal[]? withdrawals,
        Dictionary<Address, (UInt256 before, UInt256 after)>? balanceChanges,
        int accountsCreated,
        int emptyAccountsDeleted)
    {
        if (withdrawals is null || withdrawals.Length == 0)
        {
            return;
        }

        var operation = new WithdrawalsOperation
        {
            AccountsCreated = $"0x{accountsCreated:x}",
            EmptyAccountsDeleted = $"0x{emptyAccountsDeleted:x}"
        };

        UInt256 totalWithdrawn = UInt256.Zero;
        const ulong gweiToWei = 1_000_000_000; // 10^9

        foreach (var withdrawal in withdrawals)
        {
            if (withdrawal is null)
            {
                continue;
            }

            // Calculate amount in Wei (Gwei * 10^9)
            UInt256 amountInWei = withdrawal.AmountInWei;
            totalWithdrawn += amountInWei;

            var withdrawalDetail = new WithdrawalDetail
            {
                Index = $"0x{withdrawal.Index:x}",
                ValidatorIndex = $"0x{withdrawal.ValidatorIndex:x}",
                Address = withdrawal.Address,
                AmountGwei = $"0x{withdrawal.AmountInGwei:x}",
                AmountWei = $"0x{amountInWei:x}",
                GweiToWei = $"0x{gweiToWei:x}"
            };

            // Add balance change if available
            if (balanceChanges is not null && balanceChanges.TryGetValue(withdrawal.Address, out var balanceChange))
            {
                withdrawalDetail.BalanceChange = new BalanceChange
                {
                    Before = $"0x{balanceChange.before:x}",
                    After = $"0x{balanceChange.after:x}",
                    Delta = $"0x{amountInWei:x}"
                };
            }
            else
            {
                // No balance change data available - use zeros
                withdrawalDetail.BalanceChange = new BalanceChange
                {
                    Before = "0x0",
                    After = $"0x{amountInWei:x}",
                    Delta = $"0x{amountInWei:x}"
                };
            }

            operation.Withdrawals.Add(withdrawalDetail);
        }

        operation.TotalWithdrawn = $"0x{totalWithdrawn:x}";

        _tracer.TracePostExecution(operation);
    }

    /// <summary>
    /// Traces withdrawals using the actual Withdrawal objects and balance tracking.
    /// This overload provides more detailed balance change tracking.
    /// </summary>
    /// <param name="withdrawals">Array of withdrawals to process.</param>
    /// <param name="getBalanceBefore">Function to get balance before withdrawal for an address.</param>
    /// <param name="getBalanceAfter">Function to get balance after withdrawal for an address.</param>
    /// <param name="accountsCreated">Number of new accounts created for withdrawals.</param>
    /// <param name="emptyAccountsDeleted">Number of empty accounts deleted per EIP-161.</param>
    public void TraceWithdrawals(
        Withdrawal[]? withdrawals,
        Func<Address, UInt256>? getBalanceBefore,
        Func<Address, UInt256>? getBalanceAfter,
        int accountsCreated,
        int emptyAccountsDeleted)
    {
        if (withdrawals is null || withdrawals.Length == 0)
        {
            return;
        }

        var balanceChanges = new Dictionary<Address, (UInt256 before, UInt256 after)>();

        if (getBalanceBefore is not null && getBalanceAfter is not null)
        {
            foreach (var withdrawal in withdrawals)
            {
                if (withdrawal is null)
                {
                    continue;
                }

                var before = getBalanceBefore(withdrawal.Address);
                var after = getBalanceAfter(withdrawal.Address);
                balanceChanges[withdrawal.Address] = (before, after);
            }
        }

        TraceWithdrawals(withdrawals, balanceChanges, accountsCreated, emptyAccountsDeleted);
    }
}
