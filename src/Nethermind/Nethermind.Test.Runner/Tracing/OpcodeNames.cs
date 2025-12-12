// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Collections.Generic;
using System.Collections.Frozen;

namespace Nethermind.Test.Runner.Tracing;

/// <summary>
/// Opcode name lookup table matching geth's vm.OpCode.String() exactly.
/// This ensures cross-VM trace compatibility by using identical opcode names.
/// </summary>
public static class OpcodeNames
{
    private static readonly FrozenDictionary<int, string> _opcodeNames = CreateOpcodeNames();

    private static FrozenDictionary<int, string> CreateOpcodeNames()
    {
        var names = new Dictionary<int, string>
        {
            // Stop and Arithmetic Operations
            [0x00] = "STOP",
            [0x01] = "ADD",
            [0x02] = "MUL",
            [0x03] = "SUB",
            [0x04] = "DIV",
            [0x05] = "SDIV",
            [0x06] = "MOD",
            [0x07] = "SMOD",
            [0x08] = "ADDMOD",
            [0x09] = "MULMOD",
            [0x0a] = "EXP",
            [0x0b] = "SIGNEXTEND",

            // Comparison & Bitwise Logic Operations
            [0x10] = "LT",
            [0x11] = "GT",
            [0x12] = "SLT",
            [0x13] = "SGT",
            [0x14] = "EQ",
            [0x15] = "ISZERO",
            [0x16] = "AND",
            [0x17] = "OR",
            [0x18] = "XOR",
            [0x19] = "NOT",
            [0x1a] = "BYTE",
            [0x1b] = "SHL",
            [0x1c] = "SHR",
            [0x1d] = "SAR",
            [0x1e] = "CLZ", // EIP-7939: Count Leading Zeros (Osaka/Fusaka)

            // SHA3
            [0x20] = "KECCAK256",

            // Environmental Information
            [0x30] = "ADDRESS",
            [0x31] = "BALANCE",
            [0x32] = "ORIGIN",
            [0x33] = "CALLER",
            [0x34] = "CALLVALUE",
            [0x35] = "CALLDATALOAD",
            [0x36] = "CALLDATASIZE",
            [0x37] = "CALLDATACOPY",
            [0x38] = "CODESIZE",
            [0x39] = "CODECOPY",
            [0x3a] = "GASPRICE",
            [0x3b] = "EXTCODESIZE",
            [0x3c] = "EXTCODECOPY",
            [0x3d] = "RETURNDATASIZE",
            [0x3e] = "RETURNDATACOPY",
            [0x3f] = "EXTCODEHASH",

            // Block Information
            [0x40] = "BLOCKHASH",
            [0x41] = "COINBASE",
            [0x42] = "TIMESTAMP",
            [0x43] = "NUMBER",
            [0x44] = "DIFFICULTY", // Geth uses DIFFICULTY for trace compatibility (even post-merge)
            [0x45] = "GASLIMIT",
            [0x46] = "CHAINID",
            [0x47] = "SELFBALANCE",
            [0x48] = "BASEFEE",
            [0x49] = "BLOBHASH",
            [0x4a] = "BLOBBASEFEE",

            // Stack, Memory, Storage and Flow Operations
            [0x50] = "POP",
            [0x51] = "MLOAD",
            [0x52] = "MSTORE",
            [0x53] = "MSTORE8",
            [0x54] = "SLOAD",
            [0x55] = "SSTORE",
            [0x56] = "JUMP",
            [0x57] = "JUMPI",
            [0x58] = "PC",
            [0x59] = "MSIZE",
            [0x5a] = "GAS",
            [0x5b] = "JUMPDEST",
            [0x5c] = "TLOAD",
            [0x5d] = "TSTORE",
            [0x5e] = "MCOPY",
            [0x5f] = "PUSH0",

            // Push Operations
            [0x60] = "PUSH1",
            [0x61] = "PUSH2",
            [0x62] = "PUSH3",
            [0x63] = "PUSH4",
            [0x64] = "PUSH5",
            [0x65] = "PUSH6",
            [0x66] = "PUSH7",
            [0x67] = "PUSH8",
            [0x68] = "PUSH9",
            [0x69] = "PUSH10",
            [0x6a] = "PUSH11",
            [0x6b] = "PUSH12",
            [0x6c] = "PUSH13",
            [0x6d] = "PUSH14",
            [0x6e] = "PUSH15",
            [0x6f] = "PUSH16",
            [0x70] = "PUSH17",
            [0x71] = "PUSH18",
            [0x72] = "PUSH19",
            [0x73] = "PUSH20",
            [0x74] = "PUSH21",
            [0x75] = "PUSH22",
            [0x76] = "PUSH23",
            [0x77] = "PUSH24",
            [0x78] = "PUSH25",
            [0x79] = "PUSH26",
            [0x7a] = "PUSH27",
            [0x7b] = "PUSH28",
            [0x7c] = "PUSH29",
            [0x7d] = "PUSH30",
            [0x7e] = "PUSH31",
            [0x7f] = "PUSH32",

            // Duplication Operations
            [0x80] = "DUP1",
            [0x81] = "DUP2",
            [0x82] = "DUP3",
            [0x83] = "DUP4",
            [0x84] = "DUP5",
            [0x85] = "DUP6",
            [0x86] = "DUP7",
            [0x87] = "DUP8",
            [0x88] = "DUP9",
            [0x89] = "DUP10",
            [0x8a] = "DUP11",
            [0x8b] = "DUP12",
            [0x8c] = "DUP13",
            [0x8d] = "DUP14",
            [0x8e] = "DUP15",
            [0x8f] = "DUP16",

            // Exchange Operations
            [0x90] = "SWAP1",
            [0x91] = "SWAP2",
            [0x92] = "SWAP3",
            [0x93] = "SWAP4",
            [0x94] = "SWAP5",
            [0x95] = "SWAP6",
            [0x96] = "SWAP7",
            [0x97] = "SWAP8",
            [0x98] = "SWAP9",
            [0x99] = "SWAP10",
            [0x9a] = "SWAP11",
            [0x9b] = "SWAP12",
            [0x9c] = "SWAP13",
            [0x9d] = "SWAP14",
            [0x9e] = "SWAP15",
            [0x9f] = "SWAP16",

            // Logging Operations
            [0xa0] = "LOG0",
            [0xa1] = "LOG1",
            [0xa2] = "LOG2",
            [0xa3] = "LOG3",
            [0xa4] = "LOG4",

            // EOF Data Operations (EIP-7480)
            [0xd0] = "DATALOAD",
            [0xd1] = "DATALOADN",
            [0xd2] = "DATASIZE",
            [0xd3] = "DATACOPY",

            // EOF Control Flow Operations (EIP-4750, EIP-6206, EIP-663)
            [0xe0] = "RJUMP",
            [0xe1] = "RJUMPI",
            [0xe2] = "RJUMPV",
            [0xe3] = "CALLF",
            [0xe4] = "RETF",
            [0xe5] = "JUMPF",
            [0xe6] = "DUPN",
            [0xe7] = "SWAPN",
            [0xe8] = "EXCHANGE",
            [0xec] = "EOFCREATE",
            [0xee] = "RETURNCONTRACT",

            // System Operations
            [0xf0] = "CREATE",
            [0xf1] = "CALL",
            [0xf2] = "CALLCODE",
            [0xf3] = "RETURN",
            [0xf4] = "DELEGATECALL",
            [0xf5] = "CREATE2",
            [0xf7] = "RETURNDATALOAD",
            [0xf8] = "EXTCALL",
            [0xf9] = "EXTDELEGATECALL",
            [0xfa] = "STATICCALL",
            [0xfb] = "EXTSTATICCALL",
            [0xfd] = "REVERT",
            [0xfe] = "INVALID",
            [0xff] = "SELFDESTRUCT"
        };

        return names.ToFrozenDictionary();
    }

    /// <summary>
    /// Gets the opcode name from the opcode byte, matching geth's vm.OpCode(op).String() behavior.
    /// For defined opcodes, returns the standard name.
    /// For undefined opcodes, returns "opcode 0x%x not defined" format (matching geth's output).
    /// </summary>
    /// <param name="op">The opcode byte (0-255)</param>
    /// <returns>The opcode name matching geth's format</returns>
    public static string GetOpcodeName(int op)
    {
        int opcode = op & 0xFF;
        if (_opcodeNames.TryGetValue(opcode, out string? name))
        {
            return name;
        }

        // Match geth's format: "opcode 0xd not defined" (lowercase hex, no leading zero for single digit)
        return $"opcode 0x{opcode:x} not defined";
    }
}
