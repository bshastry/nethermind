# Blob Divergence Triage Tool

A high-performance parallel test runner for triaging blob-related consensus divergences across multiple Ethereum execution clients.

## Overview

This tool processes divergent test files from fuzzing campaigns through multiple EVM implementations (geth, nethermind, besu) to identify genuine consensus issues. It leverages goevmlab's `runtest` infrastructure with batch mode for maximum throughput.

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        triage-blob-divergences.sh                           │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌─────────────┐    ┌──────────────┐    ┌─────────────────────────────┐    │
│  │   Input     │    │   Batching   │    │      Parallel Execution     │    │
│  │  Scanning   │───▶│   (1000/ea)  │───▶│                             │    │
│  └─────────────┘    └──────────────┘    │  ┌─────────────────────┐   │    │
│                                          │  │     runtest         │   │    │
│  div-*.json files                        │  │  (goevmlab)         │   │    │
│  in feedback/                            │  │                     │   │    │
│                                          │  │  ┌───┐ ┌───┐ ┌───┐ │   │    │
│                                          │  │  │ G │ │ N │ │ B │ │   │    │
│                                          │  │  │ e │ │ e │ │ e │ │   │    │
│                                          │  │  │ t │ │ t │ │ s │ │   │    │
│                                          │  │  │ h │ │ h │ │ u │ │   │    │
│                                          │  │  └───┘ └───┘ └───┘ │   │    │
│                                          │  └─────────────────────┘   │    │
│                                          └─────────────────────────────┘    │
│                                                        │                    │
│                                                        ▼                    │
│                                          ┌─────────────────────────────┐    │
│                                          │   Results & Summary         │    │
│                                          │   - Divergence files        │    │
│                                          │   - Execution logs          │    │
│                                          │   - Performance metrics     │    │
│                                          └─────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Components

1. **Input Scanner**: Finds all `div-*.json` files in the feedback directory
2. **Batcher**: Splits files into manageable batches (default: 1000 files each)
3. **Parallel Executor**: Runs `runtest` with batch-mode VMs for each batch
4. **Result Aggregator**: Collects divergences and generates summary reports

### How runtest Works

The `runtest` binary from goevmlab executes state tests against multiple EVM implementations:

- **Batch Mode**: Each VM runs as a persistent process, receiving tests via stdin
- **Parallel Workers**: Multiple test dispatchers run concurrently
- **Consensus Comparison**: Results from all VMs are compared line-by-line
- **Divergence Detection**: Any mismatch triggers divergence output

## Installation

### Prerequisites

- Go 1.21+ (for building runtest)
- .NET 9.0 SDK (for nethermind)
- Java 21+ (for besu)
- `bc` command (for calculations)

### Build Dependencies

```bash
# Build geth evm tool
cd ../go-ethereum
make all

# Build nethermind test runner
cd ../nethermind/src/Nethermind
dotnet build Nethermind.Test.Runner/Nethermind.Test.Runner.csproj -c Release

# Build besu evmtool
cd ../besu
./gradlew :ethereum:evmtool:installDist

# Build runtest (auto-built by script if missing)
cd ../goevmlab/cmd/runtest
go build -o runtest .
```

## Usage

### Basic Usage

```bash
# Navigate to nethermind repo
cd /path/to/nethermind

# Run with defaults
./tools/triage-blob-divergences.sh

# Dry run (preview without execution)
./tools/triage-blob-divergences.sh --dry-run
```

### Command Line Options

| Option | Description | Default |
|--------|-------------|---------|
| `--dry-run` | Show what would be executed without running | disabled |
| `--parallel N` | Number of parallel workers | 160 |
| `--batch-size N` | Files per batch | 1000 |
| `--skip-trace` | Only compare state roots (faster) | disabled |
| `--help` | Show help message | - |

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `FEEDBACK_DIR` | Directory with divergent test files | `/dev/shm/goevmlab-hybrid-277037/feedback` |
| `GETH_BIN` | Path to geth `evm` binary | `../go-ethereum/build/bin/evm` |
| `NETH_BIN` | Path to nethermind `nethtest` binary | `src/Nethermind/artifacts/bin/Nethermind.Test.Runner/release/nethtest` |
| `BESU_BIN` | Path to besu `evmtool` binary | `../besu/ethereum/evmtool/build/install/evmtool/bin/evmtool` |
| `GOEVMLAB_DIR` | Path to goevmlab repository | `../goevmlab` |
| `OUTPUT_BASE` | Base directory for output | `/tmp/blob-triage` |
| `ORPHAN_TIMEOUT` | Timeout for stalled VMs | `30s` |

### Examples

```bash
# High-parallelism run on 192-core machine
./tools/triage-blob-divergences.sh --parallel 170

# Fast triage (skip trace comparison)
./tools/triage-blob-divergences.sh --parallel 160 --skip-trace

# Custom input directory
FEEDBACK_DIR=/path/to/divergences ./tools/triage-blob-divergences.sh

# Smaller batches for memory-constrained systems
./tools/triage-blob-divergences.sh --parallel 32 --batch-size 500

# Custom VM paths
GETH_BIN=/opt/geth/evm \
NETH_BIN=/opt/nethermind/nethtest \
BESU_BIN=/opt/besu/evmtool \
./tools/triage-blob-divergences.sh
```

## Output Structure

```
/tmp/blob-triage/triage-YYYYMMDD-HHMMSS/
├── triage.log           # Full execution log
├── summary.txt          # Summary report
├── file-list.txt        # List of all processed files
├── batches/             # Batch file lists
│   ├── batch-0000
│   ├── batch-0001
│   └── ...
└── batch-N/             # Per-batch results
    ├── execution.log    # runtest output
    └── div-*.json       # Divergence files (if any)
```

### Summary Report

The summary includes:
- Total files processed
- Divergences found
- Divergence rate percentage
- Performance metrics (tests/second)
- List of divergence files

## Performance Tuning

### Parallelism Guidelines

| CPU Cores | Recommended `--parallel` |
|-----------|-------------------------|
| 8 | 6-8 |
| 32 | 24-28 |
| 64 | 50-56 |
| 128 | 100-110 |
| 192 | 150-170 |

**Rule of thumb**: Use ~85% of available cores to leave headroom for OS and I/O.

### Memory Considerations

- Each VM instance uses ~100-200MB RAM
- With 160 parallel workers: ~30GB RAM recommended
- Reduce `--parallel` if experiencing OOM issues

### Disk I/O

- Test files are copied to staging directories
- Use fast storage (SSD, tmpfs, /dev/shm) for `FEEDBACK_DIR`
- Output directory should also be on fast storage

## Troubleshooting

### Common Issues

**Binary not found**
```
ERROR Binary 'nethermind' not found: /path/to/nethtest
```
Solution: Build the missing binary or set the correct path via environment variable.

**Argument list too long**
```
/bin/bash: /usr/bin/ls: Argument list too long
```
This is handled by the script using `find` instead of glob expansion.

**High CPU warning**
```
WARN Parallel workers (160) exceeds 2x CPU count (22)
```
Reduce `--parallel` to match available cores.

### Debug Mode

For verbose output, check the execution logs:
```bash
cat /tmp/blob-triage/triage-*/batch-*/execution.log
```

## Integration with Fuzzing Pipeline

This tool is designed to work with goevmlab's fuzzing output:

```
┌─────────────┐     ┌─────────────────┐     ┌──────────────────┐
│  goevmlab   │     │    feedback/    │     │  triage-blob-    │
│  fuzzer     │────▶│  div-*.json     │────▶│  divergences.sh  │
└─────────────┘     └─────────────────┘     └──────────────────┘
                                                     │
                                                     ▼
                                            ┌──────────────────┐
                                            │  Confirmed       │
                                            │  divergences     │
                                            └──────────────────┘
```

### Typical Workflow

1. Run fuzzing campaign with goevmlab
2. Collect divergent tests in feedback directory
3. Run triage to confirm divergences across all 3 clients
4. Investigate confirmed divergences

## License

This tool is part of the Nethermind project and is licensed under LGPL-3.0-only.
