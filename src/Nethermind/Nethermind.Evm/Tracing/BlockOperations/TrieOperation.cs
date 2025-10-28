// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Collections.Generic;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Int256;

namespace Nethermind.Evm.Tracing.BlockOperations;

/// <summary>
/// Base class for Merkle/Verkle trie computation operations
/// </summary>
public abstract class TrieOperation
{
    /// <summary>
    /// Type of trie operation (e.g., "stateRoot", "receiptRoot", "transactionRoot", "withdrawalsRoot")
    /// </summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>
    /// Type of trie structure ("merklePatricia" or "verkle")
    /// </summary>
    public string TrieType { get; set; } = "merklePatricia";

    /// <summary>
    /// Whether the calculated root matches the expected root
    /// </summary>
    public bool Valid { get; set; }

    /// <summary>
    /// Expected root hash from block header (hexadecimal)
    /// </summary>
    public string? ExpectedRoot { get; set; }
}

/// <summary>
/// State root calculation operation that computes the world state trie root
/// </summary>
public class StateRootOperation : TrieOperation
{
    /// <summary>
    /// Account updates made during block execution
    /// </summary>
    public List<AccountUpdate> AccountUpdates { get; set; } = new();

    /// <summary>
    /// Storage updates made during block execution
    /// </summary>
    public List<StorageUpdate> StorageUpdates { get; set; } = new();

    /// <summary>
    /// Final calculated state root (hexadecimal)
    /// </summary>
    public string FinalStateRoot { get; set; } = string.Empty;

    /// <summary>
    /// Expected state root from block header (hexadecimal)
    /// </summary>
    public string ExpectedStateRoot { get; set; } = string.Empty;

    public StateRootOperation()
    {
        Operation = "stateRoot";
    }
}

/// <summary>
/// Account update information showing state changes
/// </summary>
public class AccountUpdate
{
    /// <summary>
    /// Sequence number for deterministic ordering (hexadecimal)
    /// </summary>
    public string SequenceNumber { get; set; } = string.Empty;

    /// <summary>
    /// Address of the account being updated
    /// </summary>
    public Address? Address { get; set; }

    /// <summary>
    /// Keccak256(address) used as the trie key (hexadecimal)
    /// </summary>
    public string AccountTriePath { get; set; } = string.Empty;

    /// <summary>
    /// Changes to account fields
    /// </summary>
    public AccountChange AccountChange { get; set; } = new();

    /// <summary>
    /// Trie root update after this account change
    /// </summary>
    public TrieUpdate TrieUpdate { get; set; } = new();
}

/// <summary>
/// Changes to an account's fields (balance, nonce, code, storage)
/// </summary>
public class AccountChange
{
    /// <summary>
    /// Balance change information
    /// </summary>
    public FieldChange? Balance { get; set; }

    /// <summary>
    /// Nonce change information
    /// </summary>
    public FieldChange? Nonce { get; set; }

    /// <summary>
    /// Code hash change information
    /// </summary>
    public FieldChange? CodeHash { get; set; }

    /// <summary>
    /// Storage root change information
    /// </summary>
    public FieldChange? StorageRoot { get; set; }
}

/// <summary>
/// Before/after values for a field change
/// </summary>
public class FieldChange
{
    /// <summary>
    /// Value before the change (hexadecimal)
    /// </summary>
    public string Before { get; set; } = string.Empty;

    /// <summary>
    /// Value after the change (hexadecimal)
    /// </summary>
    public string After { get; set; } = string.Empty;
}

/// <summary>
/// Trie root update after an operation
/// </summary>
public class TrieUpdate
{
    /// <summary>
    /// Trie root before this update (hexadecimal)
    /// </summary>
    public string OldRoot { get; set; } = string.Empty;

    /// <summary>
    /// Trie root after this update (hexadecimal)
    /// </summary>
    public string NewRoot { get; set; } = string.Empty;
}

/// <summary>
/// Storage slot update information
/// </summary>
public class StorageUpdate
{
    /// <summary>
    /// Address of the contract whose storage is being updated
    /// </summary>
    public Address? Address { get; set; }

    /// <summary>
    /// Storage slot being updated (hexadecimal)
    /// </summary>
    public string Slot { get; set; } = string.Empty;

    /// <summary>
    /// Previous value at this slot (hexadecimal)
    /// </summary>
    public string OldValue { get; set; } = string.Empty;

    /// <summary>
    /// New value at this slot (hexadecimal)
    /// </summary>
    public string NewValue { get; set; } = string.Empty;

    /// <summary>
    /// Storage root before this update (hexadecimal)
    /// </summary>
    public string OldStorageRoot { get; set; } = string.Empty;

    /// <summary>
    /// Storage root after this update (hexadecimal)
    /// </summary>
    public string NewStorageRoot { get; set; } = string.Empty;
}

/// <summary>
/// Receipt root calculation operation
/// </summary>
public class ReceiptRootOperation : TrieOperation
{
    /// <summary>
    /// Receipt information for trie construction
    /// </summary>
    public List<ReceiptInfo> Receipts { get; set; } = new();

    /// <summary>
    /// Trie construction information
    /// </summary>
    public TrieConstruction TrieConstruction { get; set; } = new();

    /// <summary>
    /// Expected receipt root from block header (hexadecimal)
    /// </summary>
    public string ExpectedReceiptRoot { get; set; } = string.Empty;

    public ReceiptRootOperation()
    {
        Operation = "receiptRoot";
    }
}

/// <summary>
/// Receipt information for trie construction
/// </summary>
public class ReceiptInfo
{
    /// <summary>
    /// Transaction index (hexadecimal)
    /// </summary>
    public string TxIndex { get; set; } = string.Empty;

    /// <summary>
    /// Transaction type (hexadecimal)
    /// </summary>
    public string TxType { get; set; } = string.Empty;

    /// <summary>
    /// RLP encoding information
    /// </summary>
    public RlpEncoding RlpEncoding { get; set; } = new();

    /// <summary>
    /// Trie key for this receipt (RLP(txIndex)) (hexadecimal)
    /// </summary>
    public string TrieKey { get; set; } = string.Empty;

    /// <summary>
    /// Trie value (encoded receipt) (hexadecimal)
    /// </summary>
    public string TrieValue { get; set; } = string.Empty;

    /// <summary>
    /// Receipt fields
    /// </summary>
    public ReceiptFields ReceiptFields { get; set; } = new();
}

/// <summary>
/// RLP encoding information for receipts
/// </summary>
public class RlpEncoding
{
    /// <summary>
    /// Type byte for typed transactions (hexadecimal)
    /// </summary>
    public string? TypeByte { get; set; }

    /// <summary>
    /// RLP-encoded receipt data (hexadecimal)
    /// </summary>
    public string ReceiptData { get; set; } = string.Empty;

    /// <summary>
    /// Full encoding (type byte + receipt data for typed txs) (hexadecimal)
    /// </summary>
    public string FullEncoding { get; set; } = string.Empty;
}

/// <summary>
/// Receipt field values
/// </summary>
public class ReceiptFields
{
    /// <summary>
    /// Transaction status (0x0=failure, 0x1=success) (hexadecimal)
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Cumulative gas used (hexadecimal)
    /// </summary>
    public string CumulativeGasUsed { get; set; } = string.Empty;

    /// <summary>
    /// Logs bloom filter (hexadecimal)
    /// </summary>
    public string LogsBloom { get; set; } = string.Empty;

    /// <summary>
    /// Log entries
    /// </summary>
    public List<object> Logs { get; set; } = new();
}

/// <summary>
/// Trie construction information
/// </summary>
public class TrieConstruction
{
    /// <summary>
    /// Calculated root hash (hexadecimal)
    /// </summary>
    public string RootHash { get; set; } = string.Empty;
}

/// <summary>
/// Transaction root calculation operation
/// </summary>
public class TransactionRootOperation : TrieOperation
{
    /// <summary>
    /// Transaction information for trie construction
    /// </summary>
    public List<TransactionInfo> Transactions { get; set; } = new();

    /// <summary>
    /// Calculated transaction root (hexadecimal)
    /// </summary>
    public string TransactionRoot { get; set; } = string.Empty;

    /// <summary>
    /// Expected transaction root from block header (hexadecimal)
    /// </summary>
    public string ExpectedTransactionRoot { get; set; } = string.Empty;

    public TransactionRootOperation()
    {
        Operation = "transactionRoot";
    }
}

/// <summary>
/// Transaction information for trie construction
/// </summary>
public class TransactionInfo
{
    /// <summary>
    /// Transaction index (hexadecimal)
    /// </summary>
    public string TxIndex { get; set; } = string.Empty;

    /// <summary>
    /// Transaction type (hexadecimal)
    /// </summary>
    public string TxType { get; set; } = string.Empty;

    /// <summary>
    /// RLP-encoded transaction (hexadecimal)
    /// </summary>
    public string RlpEncoding { get; set; } = string.Empty;

    /// <summary>
    /// Trie key (RLP(txIndex)) (hexadecimal)
    /// </summary>
    public string TrieKey { get; set; } = string.Empty;

    /// <summary>
    /// Trie value (encoded transaction) (hexadecimal)
    /// </summary>
    public string TrieValue { get; set; } = string.Empty;
}

/// <summary>
/// Withdrawals root calculation operation (EIP-4895)
/// </summary>
public class WithdrawalsRootOperation : TrieOperation
{
    /// <summary>
    /// Withdrawal information for trie construction
    /// </summary>
    public List<WithdrawalInfo> Withdrawals { get; set; } = new();

    /// <summary>
    /// Calculated withdrawals root (hexadecimal)
    /// </summary>
    public string WithdrawalsRoot { get; set; } = string.Empty;

    /// <summary>
    /// Expected withdrawals root from block header (hexadecimal)
    /// </summary>
    public string ExpectedWithdrawalsRoot { get; set; } = string.Empty;

    public WithdrawalsRootOperation()
    {
        Operation = "withdrawalsRoot";
    }
}

/// <summary>
/// Withdrawal information for trie construction
/// </summary>
public class WithdrawalInfo
{
    /// <summary>
    /// Withdrawal index (hexadecimal)
    /// </summary>
    public string Index { get; set; } = string.Empty;

    /// <summary>
    /// RLP-encoded withdrawal (hexadecimal)
    /// </summary>
    public string RlpEncoding { get; set; } = string.Empty;

    /// <summary>
    /// Trie key (RLP(index)) (hexadecimal)
    /// </summary>
    public string TrieKey { get; set; } = string.Empty;

    /// <summary>
    /// Trie value (encoded withdrawal) (hexadecimal)
    /// </summary>
    public string TrieValue { get; set; } = string.Empty;
}
