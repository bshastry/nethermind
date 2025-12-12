# Cross-VM Corpus Validator

High-performance parallel corpus validator for cross-VM consensus verification.

## Overview

The CorpusValidator orchestrates validation of cross-client enhanced corpus entries against Nethermind's execution. It loads JSON test files, spawns parallel worker tasks, executes tests with trace normalization, and produces detailed reports comparing trace hashes across EVM implementations.

## Architecture

### Components

1. **CorpusValidator** - Main orchestrator
   - Loads corpus files from directory or single file
   - Manages worker pool using async channels
   - Provides progress reporting
   - Generates validation reports

2. **ValidationWorker** - Test execution worker
   - Parses enhanced corpus entries
   - Executes state tests using existing Nethermind infrastructure
   - Feeds traces through NormalizingTracer
   - Compares trace hashes with expected values
   - Returns ValidationResult

3. **ValidationReport** - Thread-safe result aggregation
   - Collects statistics (passed, diverged, skipped, errors)
   - Produces console summary
   - Generates JSON reports for CI integration

4. **ValidationResult** - Single test result
   - Status (Passed, Diverged, Skipped, Error)
   - Expected vs actual trace hashes and state roots
   - Execution timing
   - Error information

5. **ValidatorOptions** - Configuration
   - DumpTraces - dump divergent traces
   - OutputDir - output location
   - JsonOutput - generate JSON report
   - Quiet - suppress progress

## Usage Example

```csharp
using Nethermind.Test.Runner.Validator;

var validator = new CorpusValidator(
    corpusPath: "/path/to/corpus",
    fork: "Prague",
    workerCount: 16,
    options: new ValidatorOptions
    {
        DumpTraces = true,
        OutputDir = "./validation-output",
        JsonOutput = true
    }
);

ValidationReport report = await validator.ValidateAsync(cancellationToken);
report.PrintConsoleSummary();
```

## Implementation Details

### Async/Parallel Pattern
- Uses `Channel<T>` for lock-free job queue
- Spawns multiple async worker tasks
- Progress reporter on periodic timer
- Graceful cancellation support

### Test Execution
- Leverages existing `GeneralStateTestBase` infrastructure
- Parses tests using `JsonToEthereumTest.ConvertStateTest`
- Creates thread-local Autofac containers per execution
- Feeds traces through `NormalizingTracer`
- Gets MD5 hash via `TraceNormalizer.FinishWithResult`

### Cross-VM Metadata
- Reads `_info` field from EEST format
- Parses using `EnhancedCorpusEntry`
- Extracts `traceHash`, `stateRoot`, `generatedBy`
- Validates only tests from other clients

## Performance
- Parallel execution scales with CPU cores
- Lock-free queue minimizes contention
- Async I/O for file operations
- Thread-safe statistics aggregation

## Integration
- Drop-in replacement for Besu's CorpusValidator pattern
- Compatible with existing cross-VM corpus format
- Follows Nethermind coding conventions
- Built on standard Nethermind test infrastructure
