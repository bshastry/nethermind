# Cross-VM Corpus Validation

This module provides tooling for validating Nethermind's EVM execution against externally generated test corpora with embedded trace hash verification.

## Overview

When fuzzing or testing across multiple Ethereum clients (geth, Besu, Nethermind), we need to verify consensus by comparing execution traces. This is done by:

1. **Embedding metadata** in state test JSON files (`_info` field)
2. **Normalizing traces** to a canonical format
3. **Computing MD5 hashes** of normalized traces + stateRoot
4. **Comparing hashes** across clients

## Quick Start

```bash
# Validate a corpus against Nethermind
dotnet run --project Nethermind.Test.Runner -- \
  --validateCorpus \
  --corpusDir /path/to/corpus \
  --fork Prague

# With all options
dotnet run --project Nethermind.Test.Runner -- \
  --validateCorpus \
  --corpusDir /path/to/corpus \
  --fork Osaka \
  --workers 16 \
  --dumpTraces \
  --outputDir ./validation-results \
  --json
```

## CLI Options

| Option | Short | Description |
|--------|-------|-------------|
| `--validateCorpus` | `-v` | Enable corpus validation mode |
| `--corpusDir` | `-c` | Directory containing corpus files |
| `--fork` | | EVM fork name (Prague, Osaka, Cancun, etc.) |
| `--workers` | | Number of parallel workers (default: CPU count) |
| `--dumpTraces` | | Dump traces for divergent tests |
| `--outputDir` | `-o` | Output directory for reports and traces |
| `--json` | | Generate JSON report |
| `--quiet` | `-q` | Suppress progress output |
| `--triage` | | Enable divergence clustering |

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | All tests passed |
| 1 | Consensus divergences detected |
| 2 | Errors occurred |

## Corpus Format

State test files must contain `_info` metadata following the [EEST standard](https://github.com/ethereum/tests):

```json
{
  "testName": {
    "_info": {
      "generatedBy": "geth",
      "traceHash": "abc123def456...",
      "stateRoot": "0x1234...",
      "traceLines": 42,
      "crossvmVersion": "1.0"
    },
    "env": { ... },
    "pre": { ... },
    "transaction": { ... },
    "post": { ... }
  }
}
```

### Required Metadata Fields

| Field | Type | Description |
|-------|------|-------------|
| `generatedBy` | string | Client that generated the test |
| `traceHash` | string | MD5 hash of normalized trace |
| `stateRoot` | string | Expected post-execution state root |
| `crossvmVersion` | string | Spec version ("1.0") |

### Optional Fields

| Field | Type | Description |
|-------|------|-------------|
| `traceLines` | int | Number of trace lines in hash |
| `version` | string | Client version string |
| `fork` | string | Fork name used |
| `generatedAt` | string | ISO 8601 timestamp |

## Trace Normalization

The trace normalizer produces deterministic hashes by:

### Filtering Rules

1. **Filter depth=0** - Pre-execution markers
2. **Filter STOP (0x00)** - Virtual STOP at code end
3. **Skip duplicates** - Same (pc, depth, functionDepth)

### Canonical JSON Format

```json
{"depth":1,"pc":0,"gas":100000,"op":"0x60","opName":"PUSH1","stack":["0x1","0xff"]}
```

Field order is critical:
1. `depth` (decimal)
2. `pc` (decimal)
3. `section` (decimal, omit if 0 - EOF only)
4. `functionDepth` (decimal, omit if 0 - EOF only)
5. `gas` (decimal)
6. `op` (hex, 0x-prefixed, 2 digits, zero-padded)
7. `opName` (matches geth's vm.OpCode.String())
8. `stack` (array, last 6 items, minimal hex)

### Stack Formatting

Minimal hex representation (no leading zeros):
- Zero: `"0x0"`
- Single digit: `"0xf"` (not `"0x0f"`)
- Multi-byte: `"0xdeadbeef"`

### Hash Computation

```
MD5(
  normalized_line_1 + "\n" +
  normalized_line_2 + "\n" +
  ... +
  {"stateRoot":"0x..."} + "\n"
)
```

## Architecture

```
Nethermind.Test.Runner/
├── CrossVM/
│   ├── CrossVMMetadata.cs      # _info field model
│   └── EnhancedCorpusEntry.cs  # Test + metadata parser
├── Tracing/
│   ├── CanonicalOpLog.cs       # Normalized trace entry
│   ├── TracingResult.cs        # Hash + stateRoot + count
│   ├── OpcodeNames.cs          # Geth-compatible names
│   ├── TraceNormalizer.cs      # MD5 hash computation
│   └── NormalizingTracer.cs    # ITxTracer implementation
└── Validator/
    ├── ValidationResult.cs     # Per-test result
    ├── ValidationReport.cs     # Aggregated results
    ├── ValidatorOptions.cs     # Configuration
    ├── ValidationWorker.cs     # Parallel worker
    └── CorpusValidator.cs      # Main orchestrator
```

## Workflow

### Generating a Corpus (geth side)

```bash
# In go-ethereum
./evm statetest --trace.hash /path/to/tests/*.json
```

### Validating with Nethermind

```bash
# Quick validation
dotnet run --project Nethermind.Test.Runner -- \
  --validateCorpus --corpusDir /path/to/corpus --fork Prague

# CI integration (check exit code)
if ! dotnet run --project Nethermind.Test.Runner -- \
  --validateCorpus --corpusDir /path/to/corpus --fork Prague --quiet; then
  echo "CONSENSUS DIVERGENCE DETECTED"
  exit 1
fi
```

### Debugging Divergences

```bash
# Dump traces for divergent tests
dotnet run --project Nethermind.Test.Runner -- \
  --validateCorpus --corpusDir /path/to/corpus \
  --fork Prague --dumpTraces --outputDir ./debug

# Compare traces
diff divergent_test_geth.jsonl divergent_test_nethermind.jsonl
```

## Cross-Client Compatibility

This implementation is designed to produce identical trace hashes as:
- **geth** (`go-ethereum/tests/fuzzers/statetest/normalizer.go`)
- **Besu** (`testfuzz/src/main/java/.../tracing/TraceNormalizer.java`)

Key compatibility points:
- Opcode names match geth's `vm.OpCode.String()`
- Stack formatting matches geth's `uint256.Int.Hex()`
- Field order in JSON is deterministic
- MD5 hash includes stateRoot as final line

## See Also

- [CROSSVM_INFO_SPEC.md](../../../../besu/testfuzz/CROSSVM_INFO_SPEC.md) - Cross-VM metadata specification
- [normalizer.go](../../../../go-ethereum/tests/fuzzers/statetest/normalizer.go) - Geth reference implementation
