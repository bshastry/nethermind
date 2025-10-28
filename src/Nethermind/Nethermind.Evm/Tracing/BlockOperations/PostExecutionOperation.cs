// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Collections.Generic;
using Nethermind.Core;
using Nethermind.Int256;

namespace Nethermind.Evm.Tracing.BlockOperations;

/// <summary>
/// Base class for post-execution operations that occur after all transactions have been processed.
/// These include withdrawals (EIP-4895), execution requests (EIP-7685), and block rewards (pre-merge).
/// </summary>
public abstract class PostExecutionOperation
{
    /// <summary>
    /// Type of post-execution operation (e.g., "withdrawals", "executionRequests", "blockRewards")
    /// </summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>
    /// EIP number that defines this operation, if applicable
    /// </summary>
    public string? Eip { get; set; }
}

/// <summary>
/// Withdrawals operation (EIP-4895) that processes validator withdrawals from the beacon chain
/// </summary>
public class WithdrawalsOperation : PostExecutionOperation
{
    /// <summary>
    /// List of withdrawal operations
    /// </summary>
    public List<WithdrawalDetail> Withdrawals { get; set; } = new();

    /// <summary>
    /// Total amount withdrawn in Wei (hexadecimal)
    /// </summary>
    public string TotalWithdrawn { get; set; } = "0x0";

    /// <summary>
    /// Number of new accounts created for withdrawals
    /// </summary>
    public string AccountsCreated { get; set; } = "0x0";

    /// <summary>
    /// Number of empty accounts deleted per EIP-161
    /// </summary>
    public string EmptyAccountsDeleted { get; set; } = "0x0";

    public WithdrawalsOperation()
    {
        Operation = "withdrawals";
        Eip = "4895";
    }
}

/// <summary>
/// Details of a single withdrawal operation
/// </summary>
public class WithdrawalDetail
{
    /// <summary>
    /// Withdrawal index (hexadecimal)
    /// </summary>
    public string Index { get; set; } = string.Empty;

    /// <summary>
    /// Validator index on beacon chain (hexadecimal)
    /// </summary>
    public string ValidatorIndex { get; set; } = string.Empty;

    /// <summary>
    /// Withdrawal recipient address
    /// </summary>
    public Address? Address { get; set; }

    /// <summary>
    /// Withdrawal amount in Gwei (hexadecimal)
    /// </summary>
    public string AmountGwei { get; set; } = string.Empty;

    /// <summary>
    /// Withdrawal amount in Wei (hexadecimal)
    /// </summary>
    public string AmountWei { get; set; } = string.Empty;

    /// <summary>
    /// Conversion factor from Gwei to Wei (10^9 in hexadecimal)
    /// </summary>
    public string GweiToWei { get; set; } = "0x3b9aca00";

    /// <summary>
    /// Balance change information for the withdrawal recipient
    /// </summary>
    public BalanceChange BalanceChange { get; set; } = new();
}

/// <summary>
/// Balance change information showing before/after states
/// </summary>
public class BalanceChange
{
    /// <summary>
    /// Account balance before the operation (hexadecimal)
    /// </summary>
    public string Before { get; set; } = string.Empty;

    /// <summary>
    /// Account balance after the operation (hexadecimal)
    /// </summary>
    public string After { get; set; } = string.Empty;

    /// <summary>
    /// Balance delta (change amount) (hexadecimal)
    /// </summary>
    public string Delta { get; set; } = string.Empty;
}

/// <summary>
/// Execution requests operation (EIP-7685) that processes deposit, withdrawal, and consolidation requests
/// </summary>
public class ExecutionRequestsOperation : PostExecutionOperation
{
    /// <summary>
    /// List of execution requests
    /// </summary>
    public List<ExecutionRequest> Requests { get; set; } = new();

    /// <summary>
    /// Hash of all execution requests (hexadecimal)
    /// </summary>
    public string RequestsHash { get; set; } = string.Empty;

    /// <summary>
    /// Information about how the requests hash was calculated
    /// </summary>
    public HashCalculation HashCalculation { get; set; } = new();

    public ExecutionRequestsOperation()
    {
        Operation = "executionRequests";
        Eip = "7685";
    }
}

/// <summary>
/// Details of a single execution request (deposit, withdrawal, or consolidation)
/// </summary>
public class ExecutionRequest
{
    /// <summary>
    /// Request type byte (0x00=deposit, 0x01=withdrawal, 0x02=consolidation) (hexadecimal)
    /// </summary>
    public string RequestType { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable request type name
    /// </summary>
    public string RequestName { get; set; } = string.Empty;

    /// <summary>
    /// EIP number for this specific request type (e.g., "6110" for deposits)
    /// </summary>
    public string Eip { get; set; } = string.Empty;

    /// <summary>
    /// System call information for retrieving the request data
    /// </summary>
    public SystemCall SystemCall { get; set; } = new();

    /// <summary>
    /// Parsing information showing how request bytes are decoded
    /// </summary>
    public RequestParsing Parsing { get; set; } = new();
}

/// <summary>
/// System call information for execution requests
/// </summary>
public class SystemCall
{
    /// <summary>
    /// Address of the system contract being called
    /// </summary>
    public Address? ContractAddress { get; set; }

    /// <summary>
    /// Input data to the system call (hexadecimal)
    /// </summary>
    public string Input { get; set; } = string.Empty;

    /// <summary>
    /// Raw output from the system call (hexadecimal)
    /// </summary>
    public string Output { get; set; } = string.Empty;

    /// <summary>
    /// Gas consumed by the system call (hexadecimal)
    /// </summary>
    public string GasUsed { get; set; } = "0x0";
}

/// <summary>
/// Request parsing information showing how raw bytes are decoded into fields
/// </summary>
public class RequestParsing
{
    /// <summary>
    /// Complete request data bytes (hexadecimal)
    /// </summary>
    public string RawBytes { get; set; } = string.Empty;

    /// <summary>
    /// Parsed fields with offsets and values
    /// </summary>
    public Dictionary<string, RequestField> Fields { get; set; } = new();
}

/// <summary>
/// A single parsed field from a request
/// </summary>
public class RequestField
{
    /// <summary>
    /// Offset of this field in the raw bytes (hexadecimal)
    /// </summary>
    public string Offset { get; set; } = string.Empty;

    /// <summary>
    /// Length of this field in bytes (hexadecimal)
    /// </summary>
    public string Length { get; set; } = string.Empty;

    /// <summary>
    /// Raw value of this field (hexadecimal)
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Decoded value (e.g., for Gwei amounts), if applicable
    /// </summary>
    public string? DecodedGwei { get; set; }
}

/// <summary>
/// Hash calculation information for execution requests
/// </summary>
public class HashCalculation
{
    /// <summary>
    /// Whether requests were sorted before hashing
    /// </summary>
    public bool SortedRequests { get; set; }

    /// <summary>
    /// Hash method used (e.g., "sha256")
    /// </summary>
    public string HashMethod { get; set; } = "sha256";
}

/// <summary>
/// Block rewards operation (pre-merge only) for coinbase and ommer rewards
/// </summary>
public class BlockRewardsOperation : PostExecutionOperation
{
    /// <summary>
    /// Address receiving the block reward (coinbase/miner)
    /// </summary>
    public Address? MiningBeneficiary { get; set; }

    /// <summary>
    /// Coinbase reward information
    /// </summary>
    public CoinbaseReward CoinbaseReward { get; set; } = new();

    /// <summary>
    /// List of ommer (uncle) rewards
    /// </summary>
    public List<OmmerReward> OmmerRewards { get; set; } = new();

    /// <summary>
    /// Balance changes for all affected accounts
    /// </summary>
    public List<RewardBalanceChange> BalanceChanges { get; set; } = new();

    public BlockRewardsOperation()
    {
        Operation = "blockRewards";
    }
}

/// <summary>
/// Coinbase reward information for the block producer
/// </summary>
public class CoinbaseReward
{
    /// <summary>
    /// Base block reward for this fork (hexadecimal)
    /// </summary>
    public string BaseReward { get; set; } = string.Empty;

    /// <summary>
    /// Bonus reward for including ommers (hexadecimal)
    /// </summary>
    public string OmmerInclusionReward { get; set; } = string.Empty;

    /// <summary>
    /// Total reward to beneficiary (hexadecimal)
    /// </summary>
    public string TotalReward { get; set; } = string.Empty;

    /// <summary>
    /// Formula used for calculation
    /// </summary>
    public string Formula { get; set; } = string.Empty;
}

/// <summary>
/// Ommer (uncle) reward information
/// </summary>
public class OmmerReward
{
    /// <summary>
    /// Index of this ommer in the block (hexadecimal)
    /// </summary>
    public string OmmerIndex { get; set; } = string.Empty;

    /// <summary>
    /// Address receiving the ommer reward
    /// </summary>
    public Address? OmmerBeneficiary { get; set; }

    /// <summary>
    /// Block number of the ommer (hexadecimal)
    /// </summary>
    public string OmmerNumber { get; set; } = string.Empty;

    /// <summary>
    /// Current block number (hexadecimal)
    /// </summary>
    public string CurrentNumber { get; set; } = string.Empty;

    /// <summary>
    /// Difference between current and ommer block numbers (hexadecimal)
    /// </summary>
    public string NumberDelta { get; set; } = string.Empty;

    /// <summary>
    /// Ommer reward amount (hexadecimal)
    /// </summary>
    public string Reward { get; set; } = string.Empty;

    /// <summary>
    /// Formula used for calculation
    /// </summary>
    public string Formula { get; set; } = string.Empty;
}

/// <summary>
/// Balance change for a reward recipient
/// </summary>
public class RewardBalanceChange
{
    /// <summary>
    /// Address of the account
    /// </summary>
    public Address? Address { get; set; }

    /// <summary>
    /// Balance before the reward (hexadecimal)
    /// </summary>
    public string Before { get; set; } = string.Empty;

    /// <summary>
    /// Balance after the reward (hexadecimal)
    /// </summary>
    public string After { get; set; } = string.Empty;

    /// <summary>
    /// Balance delta (reward amount) (hexadecimal)
    /// </summary>
    public string Delta { get; set; } = string.Empty;
}
