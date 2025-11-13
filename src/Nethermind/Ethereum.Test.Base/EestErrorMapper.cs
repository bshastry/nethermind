// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Collections.Generic;
using System.Text.RegularExpressions;
using Nethermind.Core;

namespace Ethereum.Test.Base;

/// <summary>
/// Maps Nethermind validation errors to EEST (Ethereum Execution Spec Tests) canonical error codes.
/// Provides structured error details for standardized test output.
/// </summary>
public static class EestErrorMapper
{
    private static readonly Dictionary<string, (string code, string message)> ErrorMappings = new()
    {
        // ===== BlockException Mappings =====

        // Basic Block Header Validation
        ["InvalidBlockNumber"] = ("BlockException.INVALID_BLOCK_NUMBER", "Block number does not match parent + 1"),
        ["block number is not parent + 1"] = ("BlockException.INVALID_BLOCK_NUMBER", "Block number does not match parent + 1"),
        ["InvalidBlockHash"] = ("BlockException.INVALID_BLOCK_HASH", "Block hash does not match computed hash"),
        ["InvalidHeaderHash"] = ("BlockException.INVALID_BLOCK_HASH", "Header hash does not match computed hash"),
        ["InvalidTimestamp"] = ("BlockException.INVALID_BLOCK_TIMESTAMP_OLDER_THAN_PARENT", "Block timestamp is older than parent"),
        ["timestamp is older than parent"] = ("BlockException.INVALID_BLOCK_TIMESTAMP_OLDER_THAN_PARENT", "Block timestamp is older than parent"),
        ["timestamp cannot be lower"] = ("BlockException.INVALID_BLOCK_TIMESTAMP_OLDER_THAN_PARENT", "Block timestamp is older than parent"),

        // Parent/Ancestry Validation
        ["InvalidAncestor"] = ("BlockException.UNKNOWN_PARENT", "No valid ancestors could be found"),
        ["Mismatched parent"] = ("BlockException.UNKNOWN_PARENT", "Block parent hash does not match expected parent"),
        ["InvalidGenesisBlock"] = ("BlockException.INVALID_BLOCK_NUMBER", "Genesis block could not be validated"),

        // Extra Data Validation
        ["InvalidExtraData"] = ("BlockException.EXTRA_DATA_TOO_BIG", "Extra data in header is not valid"),
        ["extra data.*not valid"] = ("BlockException.EXTRA_DATA_TOO_BIG", "Extra data exceeds maximum size"),

        // Gas Limit & Gas Used Validation
        ["InvalidGasLimit"] = ("BlockException.INVALID_GASLIMIT", "Block gas limit exceeds protocol maximum or invalid adjustment"),
        ["gas limit is not correct"] = ("BlockException.INVALID_GASLIMIT", "Block gas limit does not match formula"),
        ["ExceededGasLimit"] = ("BlockException.INVALID_GAS_USED_ABOVE_LIMIT", "Gas used exceeds gas limit"),
        ["Gas used exceeds gas limit"] = ("BlockException.INVALID_GAS_USED_ABOVE_LIMIT", "Gas used exceeds block gas limit"),
        ["HeaderGasUsedMismatch"] = ("BlockException.INVALID_GAS_USED", "Gas used in header does not match calculated"),
        ["Gas used in header does not match"] = ("BlockException.INVALID_GAS_USED", "Gas used does not match actual usage"),
        ["NegativeGasLimit"] = ("BlockException.INVALID_GASLIMIT", "Gas limit cannot be negative"),
        ["NegativeGasUsed"] = ("BlockException.INVALID_GAS_USED", "Gas used cannot be negative"),

        // Difficulty & Base Fee (EIP-1559)
        ["InvalidDifficulty"] = ("BlockException.INVALID_DIFFICULTY", "Block difficulty does not match expected value"),
        ["InvalidTotalDifficulty"] = ("BlockException.INVALID_DIFFICULTY", "Total difficulty could not be validated"),
        ["InvalidBaseFeePerGas"] = ("BlockException.INVALID_BASEFEE_PER_GAS", "Base fee per gas does not match calculated value"),
        ["Does not match calculated"] = ("BlockException.INVALID_BASEFEE_PER_GAS", "Base fee calculation incorrect"),

        // Seal Parameters
        ["InvalidSealParameters"] = ("BlockException.INVALID_DIFFICULTY", "Seal parameters could not be validated"),

        // Merkle Root Validation
        ["InvalidLogsBloom"] = ("BlockException.INVALID_LOG_BLOOM", "Logs bloom filter does not match computed value"),
        ["Logs bloom.*does not match"] = ("BlockException.INVALID_LOG_BLOOM", "Logs bloom hash mismatch"),
        ["InvalidStateRoot"] = ("BlockException.INVALID_STATE_ROOT", "State root does not match expected value"),
        ["State root.*does not match"] = ("BlockException.INVALID_STATE_ROOT", "State root hash mismatch"),
        ["InvalidReceiptsRoot"] = ("BlockException.INVALID_RECEIPTS_ROOT", "Receipts root does not match computed value"),
        ["Receipts root.*does not match"] = ("BlockException.INVALID_RECEIPTS_ROOT", "Receipts root hash mismatch"),
        ["InvalidTransactionsRoot"] = ("BlockException.INVALID_TRANSACTIONS_ROOT", "Transactions root does not match computed value"),
        ["InvalidTxRoot"] = ("BlockException.INVALID_TRANSACTIONS_ROOT", "Transactions root hash mismatch"),
        ["InvalidUnclesHash"] = ("BlockException.INVALID_UNCLES_HASH", "Uncle header hash does not match computed value"),
        ["Uncle header hash does not match"] = ("BlockException.INVALID_UNCLES_HASH", "Uncle hash mismatch"),

        // Uncle Validation
        ["ExceededUncleLimit"] = ("BlockException.TOO_MANY_UNCLES", "Block declares too many uncles over allowed limit"),
        ["Cannot have more than.*uncles"] = ("BlockException.TOO_MANY_UNCLES", "Too many uncles in block"),
        ["InvalidUncle"] = ("BlockException.INVALID_UNCLES_HASH", "Uncle could not be validated"),

        // Withdrawals (EIP-4895)
        ["InvalidWithdrawalsRoot"] = ("BlockException.INVALID_WITHDRAWALS_ROOT", "Withdrawals root does not match computed value"),
        ["withdrawals.*expected.*got"] = ("BlockException.INVALID_WITHDRAWALS_ROOT", "Withdrawals root mismatch"),
        ["MissingWithdrawals"] = ("BlockException.INVALID_WITHDRAWALS_ROOT", "Block body is missing withdrawals"),
        ["missing withdrawals"] = ("BlockException.INVALID_WITHDRAWALS_ROOT", "Withdrawals required in Shanghai+"),
        ["WithdrawalsNotEnabled"] = ("BlockException.INVALID_WITHDRAWALS_ROOT", "Block body cannot have withdrawals"),
        ["cannot have withdrawals"] = ("BlockException.INVALID_WITHDRAWALS_ROOT", "Withdrawals not allowed pre-Shanghai"),

        // Parent Beacon Block Root (EIP-4788)
        ["InvalidParentBeaconBlockRoot"] = ("BlockException.INVALID_BLOCK_HASH", "Beacon block root in header does not match"),
        ["Beacon block root.*does not match"] = ("BlockException.INVALID_BLOCK_HASH", "Parent beacon block root mismatch"),

        // Blob Gas Field Validation (EIP-4844)
        ["MissingBlobGasUsed"] = ("BlockException.INCORRECT_BLOCK_FORMAT", "BlobGasUsed must be set in header for Cancun+"),
        ["Must be set in header"] = ("BlockException.INCORRECT_BLOCK_FORMAT", "BlobGasUsed required in Cancun+"),
        ["NotAllowedBlobGasUsed"] = ("BlockException.INCORRECT_BLOCK_FORMAT", "BlobGasUsed cannot be set before Cancun"),
        ["MissingExcessBlobGas"] = ("BlockException.INCORRECT_BLOCK_FORMAT", "ExcessBlobGas must be set in header for Cancun+"),
        ["NotAllowedExcessBlobGas"] = ("BlockException.INCORRECT_BLOCK_FORMAT", "ExcessBlobGas cannot be set before Cancun"),

        // Blob Gas Calculation & Limits (EIP-4844)
        ["InvalidExcessBlobGas"] = ("BlockException.INCORRECT_EXCESS_BLOB_GAS", "Excess blob gas calculation incorrect"),
        ["HeaderExcessBlobGasMismatch"] = ("BlockException.INCORRECT_EXCESS_BLOB_GAS", "Excess blob gas in header does not match calculated"),
        ["Excess blob gas.*does not match"] = ("BlockException.INCORRECT_EXCESS_BLOB_GAS", "Excess blob gas mismatch"),
        ["BlobGasUsedAboveLimit"] = ("BlockException.BLOB_GAS_USED_ABOVE_LIMIT", "Total blob gas used exceeds block limit"),
        ["BlockBlobGasExceeded"] = ("BlockException.BLOB_GAS_USED_ABOVE_LIMIT", "Block blob gas used above limit"),
        ["cannot have more than.*blob gas"] = ("BlockException.BLOB_GAS_USED_ABOVE_LIMIT", "Blob gas limit exceeded"),
        ["HeaderBlobGasMismatch"] = ("BlockException.INCORRECT_BLOB_GAS_USED", "Blob gas used in header does not match calculated"),
        ["Blob gas.*does not match calculated"] = ("BlockException.INCORRECT_BLOB_GAS_USED", "Blob gas used mismatch"),
        ["BlobGasPriceOverflow"] = ("BlockException.INCORRECT_EXCESS_BLOB_GAS", "Overflow in excess blob gas calculation"),
        ["InsufficientMaxFeePerBlobGas"] = ("TransactionException.INSUFFICIENT_MAX_FEE_PER_BLOB_GAS", "Not enough to cover blob gas fee"),

        // Requests (EIP-7685)
        ["InvalidRequestsHash"] = ("BlockException.INVALID_REQUESTS", "Requests hash mismatch in block"),
        ["Requests hash mismatch"] = ("BlockException.INVALID_REQUESTS", "Requests hash does not match calculated"),
        ["MissingRequests"] = ("BlockException.INVALID_REQUESTS", "Requests cannot be null when EIP-6110 or EIP-7002 activated"),
        ["Requests cannot be null"] = ("BlockException.INVALID_REQUESTS", "Requests required in Prague+"),
        ["RequestsNotEnabled"] = ("BlockException.INVALID_REQUESTS", "Requests must be null when EIP-6110 and EIP-7002 not activated"),
        ["Requests must be null"] = ("BlockException.INVALID_REQUESTS", "Requests not allowed pre-Prague"),
        ["InvalidRequestsOrder"] = ("BlockException.INVALID_REQUESTS", "Requests are not in correct order"),

        // Transaction in Block Validation
        ["InvalidTxInBlock"] = ("BlockException.INVALID_TRANSACTIONS_ROOT", "Transaction at index in body is invalid"),
        ["Tx at index.*in body"] = ("BlockException.INVALID_TRANSACTIONS_ROOT", "Invalid transaction in block"),

        // Receipt Validation
        ["ReceiptCountMismatch"] = ("BlockException.INVALID_RECEIPTS_ROOT", "Receipt count does not match transaction count"),

        // Block Size & Limits
        ["ExceededBlockSizeLimit"] = ("BlockException.RLP_BLOCK_LIMIT_EXCEEDED", "Block RLP encoding exceeds size limit"),
        ["Exceeded block size limit"] = ("BlockException.RLP_BLOCK_LIMIT_EXCEEDED", "Block too large"),
        ["NegativeBlockNumber"] = ("BlockException.INVALID_BLOCK_NUMBER", "Block number cannot be negative"),

        // System Contracts
        ["WithdrawalsContractEmpty"] = ("BlockException.SYSTEM_CONTRACT_EMPTY", "Withdrawals contract not deployed"),
        ["Contract is not deployed"] = ("BlockException.SYSTEM_CONTRACT_EMPTY", "System contract address contains no code"),
        ["WithdrawalsContractFailed"] = ("BlockException.SYSTEM_CONTRACT_CALL_FAILED", "Withdrawals contract execution failed"),
        ["ConsolidationsContractEmpty"] = ("BlockException.SYSTEM_CONTRACT_EMPTY", "Consolidations contract not deployed"),
        ["ConsolidationsContractFailed"] = ("BlockException.SYSTEM_CONTRACT_CALL_FAILED", "Consolidations contract execution failed"),
        ["Contract execution failed"] = ("BlockException.SYSTEM_CONTRACT_CALL_FAILED", "System contract call failed"),

        // Deposit Events (EIP-6110)
        ["DepositsInvalid"] = ("BlockException.INVALID_DEPOSIT_EVENT_LAYOUT", "Invalid deposit event layout"),
        ["Invalid deposit event"] = ("BlockException.INVALID_DEPOSIT_EVENT_LAYOUT", "Deposit event layout does not match required format"),

        // ===== TransactionException Mappings =====

        // Basic Transaction Validation
        ["InvalidTxType"] = ("TransactionException.TYPE_NOT_SUPPORTED", "Transaction type is not supported"),
        ["Transaction type.*not supported"] = ("TransactionException.TYPE_NOT_SUPPORTED", "Transaction type not supported on this chain"),
        ["InvalidTxSignature"] = ("TransactionException.INVALID_SIGNATURE_VRS", "Transaction signature is invalid"),
        ["InvalidSignature"] = ("TransactionException.INVALID_SIGNATURE_VRS", "Transaction signature is invalid"),
        ["Signature is invalid"] = ("TransactionException.INVALID_SIGNATURE_VRS", "Invalid transaction v, r, s values"),
        ["SenderNotEOA"] = ("TransactionException.SENDER_NOT_EOA", "Transaction sender is not an externally owned account"),

        // Nonce Validation
        ["NonceTooLow"] = ("TransactionException.NONCE_MISMATCH_TOO_LOW", "Transaction nonce is lower than account nonce"),
        ["nonce too low"] = ("TransactionException.NONCE_MISMATCH_TOO_LOW", "Transaction nonce < sender nonce"),
        ["NonceTooHigh"] = ("TransactionException.NONCE_MISMATCH_TOO_HIGH", "Transaction nonce is higher than expected"),
        ["nonce too high"] = ("TransactionException.NONCE_MISMATCH_TOO_HIGH", "Transaction nonce > sender nonce"),
        ["Nonce exceeds max"] = ("TransactionException.NONCE_TOO_BIG", "Transaction nonce exceeds maximum value"),

        // Gas & Fee Validation
        ["IntrinsicGas"] = ("TransactionException.INTRINSIC_GAS_TOO_LOW", "Transaction intrinsic gas exceeds gas limit"),
        ["intrinsic gas too low"] = ("TransactionException.INTRINSIC_GAS_TOO_LOW", "Transaction gas limit is too low"),
        ["InsufficientFunds"] = ("TransactionException.INSUFFICIENT_ACCOUNT_FUNDS", "Account has insufficient funds for transaction"),
        ["insufficient funds"] = ("TransactionException.INSUFFICIENT_ACCOUNT_FUNDS", "Sender does not have enough funds"),
        ["GasPriceLessThanBaseFee"] = ("TransactionException.INSUFFICIENT_MAX_FEE_PER_GAS", "Transaction gas price is less than block base fee"),
        ["gas price less than"] = ("TransactionException.INSUFFICIENT_MAX_FEE_PER_GAS", "Max fee per gas lower than base fee"),
        ["MaxPriorityFeePerGasTooHigh"] = ("TransactionException.PRIORITY_GREATER_THAN_MAX_FEE_PER_GAS", "Max priority fee exceeds max fee per gas"),
        ["InvalidMaxPriorityFeePerGas"] = ("TransactionException.PRIORITY_GREATER_THAN_MAX_FEE_PER_GAS", "Priority fee cannot be higher than max fee"),
        ["Cannot be higher than maxFeePerGas"] = ("TransactionException.PRIORITY_GREATER_THAN_MAX_FEE_PER_GAS", "Priority fee greater than max fee"),
        ["TxGasLimitCapExceeded"] = ("TransactionException.GAS_LIMIT_EXCEEDS_MAXIMUM", "Gas limit exceeds maximum allowed limit"),
        ["Gas limit.*exceeded cap"] = ("TransactionException.GAS_LIMIT_EXCEEDS_MAXIMUM", "Transaction gas limit exceeds cap"),

        // Chain ID Validation
        ["InvalidTxChainId"] = ("TransactionException.INVALID_CHAINID", "Transaction chain ID is incorrect"),
        ["Expected.*got.*chain"] = ("TransactionException.INVALID_CHAINID", "Chain ID mismatch"),

        // Contract Size & Initcode (EIP-3860)
        ["ContractSizeTooBig"] = ("TransactionException.INITCODE_SIZE_EXCEEDED", "Contract creation initcode size exceeded"),
        ["max initcode size exceeded"] = ("TransactionException.INITCODE_SIZE_EXCEEDED", "Initcode too large for contract creation"),

        // Type 3 Blob Transaction Validation (EIP-4844)
        ["TooManyBlobs"] = ("TransactionException.TYPE_3_TX_BLOB_COUNT_EXCEEDED", "Transaction has too many blob hashes"),
        ["too many blobs"] = ("TransactionException.TYPE_3_TX_BLOB_COUNT_EXCEEDED", "Blob count exceeded"),
        ["TxMissingTo"] = ("TransactionException.TYPE_3_TX_CONTRACT_CREATION", "Blob transaction cannot be contract creation"),
        ["blob transaction of type create"] = ("TransactionException.TYPE_3_TX_CONTRACT_CREATION", "Type 3 transaction has empty to field"),
        ["BlobTxMissingMaxFeePerBlobGas"] = ("TransactionException.TYPE_3_TX_INVALID_BLOB_VERSIONED_HASH", "Blob transaction missing max fee per blob gas"),
        ["BlobTxMissingBlobVersionedHashes"] = ("TransactionException.TYPE_3_TX_ZERO_BLOBS", "Blob transaction missing blob hashes"),
        ["blob transaction missing blob hashes"] = ("TransactionException.TYPE_3_TX_ZERO_BLOBS", "Blob transaction has no blobs"),
        ["MissingBlobVersionedHash"] = ("TransactionException.TYPE_3_TX_INVALID_BLOB_VERSIONED_HASH", "Blob versioned hash must be set"),
        ["InvalidBlobVersionedHashSize"] = ("TransactionException.TYPE_3_TX_INVALID_BLOB_VERSIONED_HASH", "Blob versioned hash size invalid"),
        ["Cannot exceed.*blob"] = ("TransactionException.TYPE_3_TX_INVALID_BLOB_VERSIONED_HASH", "Blob versioned hash exceeds size limit"),
        ["InvalidBlobVersionedHashVersion"] = ("TransactionException.TYPE_3_TX_INVALID_BLOB_VERSIONED_HASH", "Blob version not supported"),
        ["InvalidBlobVersionedHash"] = ("TransactionException.TYPE_3_TX_INVALID_BLOB_VERSIONED_HASH", "Blob versioned hash is invalid"),
        ["Blob version not supported"] = ("TransactionException.TYPE_3_TX_INVALID_BLOB_VERSIONED_HASH", "Invalid blob versioned hash version"),
        ["BlobTxGasLimitExceeded"] = ("TransactionException.TYPE_3_TX_MAX_BLOB_GAS_ALLOWANCE_EXCEEDED", "Transaction blob gas exceeds per-transaction limit"),
        ["totalDataGas.*exceeded MaxBlobGas"] = ("TransactionException.TYPE_3_TX_MAX_BLOB_GAS_ALLOWANCE_EXCEEDED", "Blob gas allowance exceeded"),
        ["BlobTxMissingBlobs"] = ("TransactionException.TYPE_3_TX_ZERO_BLOBS", "Blob transaction missing blobs"),
        ["NotAllowedMaxFeePerBlobGas"] = ("TransactionException.TYPE_3_TX_PRE_FORK", "MaxFeePerBlobGas cannot be set before Cancun"),
        ["NotAllowedBlobVersionedHashes"] = ("TransactionException.TYPE_3_TX_PRE_FORK", "BlobVersionedHashes cannot be set before Cancun"),
        ["InvalidBlobDataSize"] = ("TransactionException.TYPE_3_TX_INVALID_BLOB_VERSIONED_HASH", "Blob data fields are of incorrect size"),
        ["InvalidBlobHashes"] = ("TransactionException.TYPE_3_TX_INVALID_BLOB_VERSIONED_HASH", "Blob proof hashes do not match"),
        ["InvalidBlobProofs"] = ("TransactionException.TYPE_3_TX_INVALID_BLOB_VERSIONED_HASH", "Blob proofs do not match"),
        ["Hashes do not match the blobs"] = ("TransactionException.TYPE_3_TX_INVALID_BLOB_VERSIONED_HASH", "Blob hash mismatch"),
        ["Proofs do not match the blobs"] = ("TransactionException.TYPE_3_TX_INVALID_BLOB_VERSIONED_HASH", "Blob proof mismatch"),
        ["InvalidBlobCommitmentHash"] = ("TransactionException.TYPE_3_TX_INVALID_BLOB_VERSIONED_HASH", "Commitment hash does not match"),
        ["InvalidProofVersion"] = ("TransactionException.TYPE_3_TX_WITH_FULL_BLOBS", "Version of network wrapper not supported"),
        ["InvalidTransactionForm"] = ("TransactionException.TYPE_3_TX_WITH_FULL_BLOBS", "Transaction cannot be ShardBlobNetworkWrapper"),
        ["Cannot be ShardBlobNetworkWrapper"] = ("TransactionException.TYPE_3_TX_WITH_FULL_BLOBS", "Invalid transaction form"),

        // Type 4 SetCode Transaction Validation (EIP-7702)
        ["NotAllowedCreateTransaction"] = ("TransactionException.TYPE_4_TX_CONTRACT_CREATION", "SetCode transaction cannot be contract creation"),
        ["To must be set"] = ("TransactionException.TYPE_4_TX_CONTRACT_CREATION", "Type 4 transaction has empty to field"),
        ["NotAllowedAuthorizationList"] = ("TransactionException.TYPE_4_INVALID_AUTHORIZATION_FORMAT", "Authorization list only allowed for SetCode transactions"),
        ["Only transactions with type"] = ("TransactionException.TYPE_4_INVALID_AUTHORIZATION_FORMAT", "Authorization list not allowed for this transaction type"),
        ["MissingAuthorizationList"] = ("TransactionException.TYPE_4_EMPTY_AUTHORIZATION_LIST", "Authorization list must be set for SetCode transaction"),
        ["InvalidAuthorizationTupleFormat"] = ("TransactionException.TYPE_4_INVALID_AUTHORIZATION_FORMAT", "Authorization tuple format is invalid"),
        ["InvalidAuthoritySignature"] = ("TransactionException.TYPE_4_INVALID_AUTHORITY_SIGNATURE", "Invalid signature in authorization list"),
        ["Invalid signature in authorization"] = ("TransactionException.TYPE_4_INVALID_AUTHORITY_SIGNATURE", "Authority signature is invalid"),

        // EOF Validation
        ["InvalidCreateTxData"] = ("TransactionException.INITCODE_SIZE_EXCEEDED", "Legacy create transaction cannot create EOF code"),
        ["Legacy createTx cannot create Eof"] = ("TransactionException.INITCODE_SIZE_EXCEEDED", "EOF code in legacy transaction"),
        ["TooManyEofInitcodes"] = ("TransactionException.INITCODE_SIZE_EXCEEDED", "EOF initcodes count exceeded limit"),
        ["EmptyEofInitcodesField"] = ("TransactionException.INITCODE_SIZE_EXCEEDED", "EOF initcodes count must be greater than 0"),
        ["EofContractSizeInvalid"] = ("TransactionException.INITCODE_SIZE_EXCEEDED", "EOF initcode size is invalid"),

        // Generic fallback
        ["Unknown"] = ("UndefinedException", "Unknown error occurred"),
    };

    /// <summary>
    /// Maps a Nethermind validation error message to EEST canonical error details.
    /// </summary>
    /// <param name="errorMessage">The error message from block validation</param>
    /// <returns>Structured error details with EEST error code, message, and context</returns>
    public static ErrorDetails? MapErrorToEEST(string? errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage))
            return null;

        var details = new ErrorDetails
        {
            Context = null  // Don't include context to match geth's minimal format
        };

        // Check if error message already contains a formatted EEST code (e.g., "BlockException.INVALID_BLOCK_TIMESTAMP_OLDER_THAN_PARENT")
        if (errorMessage.Contains("BlockException.", System.StringComparison.Ordinal) ||
            errorMessage.Contains("TransactionException.", System.StringComparison.Ordinal))
        {
            // Extract the EEST code (format: "...Exception.ERROR_NAME...")
            int exceptionPos = errorMessage.IndexOf("Exception.", System.StringComparison.Ordinal);
            if (exceptionPos >= 0)
            {
                int startPos = errorMessage.LastIndexOf(' ', exceptionPos) + 1;
                if (startPos == 0) startPos = 0;  // Handle case where Exception is at the start
                string remaining = errorMessage.Substring(startPos);
                // Extract until next space, comma, or end of string
                int endPos = remaining.IndexOfAny([' ', ',', '\n', '\r']);
                string code = endPos > 0 ? remaining.Substring(0, endPos) : remaining;
                details.Code = code.Trim();
                details.Message = "Expected exception did not occur";
                return details;
            }
        }

        // Try to find matching error code
        foreach (var kvp in ErrorMappings)
        {
            if (errorMessage.Contains(kvp.Key, System.StringComparison.OrdinalIgnoreCase))
            {
                details.Code = kvp.Value.code;
                details.Message = kvp.Value.message;
                return details;
            }
        }

        // Fallback: use generic undefined error with simple message (per EEST spec)
        details.Code = "UndefinedException";
        details.Message = "Unknown error occurred";
        return details;
    }

    private static void ExtractContext(string errorMessage, ErrorDetails details)
    {
        // Extract block number context
        var blockNumPattern = @"block(?:Number|Num)?\s*(?:is\s*)?(?:0x)?([0-9a-fA-F]+)";
        var matches = Regex.Matches(errorMessage, blockNumPattern);
        if (matches.Count > 0)
        {
            if (matches.Count >= 1 && !details.Context.ContainsKey("blockNumber"))
            {
                details.Context["blockNumber"] = "0x" + matches[0].Groups[1].Value;
            }
            if (matches.Count >= 2)
            {
                details.Context["parentNumber"] = "0x" + matches[1].Groups[1].Value;
            }
        }

        // Extract expected/actual values
        var expectedMatch = Regex.Match(errorMessage, @"expected[:\s]+(?:0x)?([0-9a-fA-F]+)", RegexOptions.IgnoreCase);
        if (expectedMatch.Success)
        {
            details.Context["expected"] = "0x" + expectedMatch.Groups[1].Value;
        }

        var actualMatch = Regex.Match(errorMessage, @"actual[:\s]+(?:0x)?([0-9a-fA-F]+)", RegexOptions.IgnoreCase);
        if (actualMatch.Success)
        {
            details.Context["actual"] = "0x" + actualMatch.Groups[1].Value;
        }

        // Extract hash values (for state root, block hash, etc.)
        var hashPattern = @"(?:hash|root|Hash|Root)[:\s]+(0x[0-9a-fA-F]{64})";
        var hashMatches = Regex.Matches(errorMessage, hashPattern);
        if (hashMatches.Count > 0)
        {
            details.Context["hash"] = hashMatches[0].Groups[1].Value;
        }
    }
}
