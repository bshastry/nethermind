# Nethermind Test Runner (nethtest)

`nethtest` is Nethermind's command-line tool for executing Ethereum Foundation test suites, including state tests, block tests, and EOF tests. It provides compatibility testing for Ethereum protocol implementations.

## Features

- **State Tests**: Execute single-transaction state transition tests
- **Block Tests**: Execute multi-block blockchain tests with consolidated tracing
- **EOF Tests**: Execute EVM Object Format tests
- **Tracing Support**: Generate execution traces compatible with go-ethereum's format
- **Flexible Filtering**: Run specific tests using regex patterns
- **Network Support**: Test against both Ethereum mainnet and Gnosis chain specs

## Building

From the repository root:

```bash
dotnet build src/Nethermind/Nethermind.Test.Runner/Nethermind.Test.Runner.csproj -c Release
```

The compiled binary will be at:
```
src/Nethermind/artifacts/bin/Nethermind.Test.Runner/release/nethtest.dll
```

## Usage

### Basic Command Structure

```bash
dotnet nethtest.dll [options]
```

### Command-Line Options

| Option | Alias | Description |
|--------|-------|-------------|
| `--input <path>` | `-i` | Input file or directory containing tests (required) |
| `--filter <regex>` | `-f` | Filter tests by name using regular expression |
| `--blockTest` | `-b` | Run as blockchain test (default: state test) |
| `--eofTest` | `-e` | Run as EOF test (default: state test) |
| `--trace` | `-t` | Enable tracing (always trace, not just on failures) |
| `--neverTrace` | `-n` | Never trace (only for state tests) |
| `--memory` | `-m` | Exclude memory from traces |
| `--stack` | `-s` | Exclude stack from traces |
| `--stdin` | `-x` | Read test filenames from stdin |
| `--wait` | `-w` | Wait for input after test run |
| `--gnosisTest` | `-g` | Use Gnosis chain specification |
| `--warmup` | `-wu` | Enable warmup for benchmarking |

## Test Types

### State Tests

State tests verify single transaction execution and state transitions. Traces are written to **stderr** on test failures (or always with `-t`).

**Run state tests:**
```bash
dotnet nethtest.dll -i /path/to/GeneralStateTests/
```

**Run with tracing (on failures by default):**
```bash
dotnet nethtest.dll -i /path/to/statetest.json
```

**Always trace:**
```bash
dotnet nethtest.dll -i /path/to/statetest.json -t
```

**Never trace:**
```bash
dotnet nethtest.dll -i /path/to/statetest.json -n
```

**Include memory in traces:**
```bash
dotnet nethtest.dll -i /path/to/statetest.json -t -m
```

### Block Tests

Block tests verify multi-block blockchain execution with consolidated transaction tracing.

**Run block tests:**
```bash
dotnet nethtest.dll -b -i /path/to/BlockchainTests/
```

**Run with transaction tracing:**
```bash
dotnet nethtest.dll -b -i /path/to/blocktest.json -t
```

**Trace with memory enabled:**
```bash
dotnet nethtest.dll -b -i /path/to/blocktest.json -t -m
```

**Trace with stack disabled:**
```bash
dotnet nethtest.dll -b -i /path/to/blocktest.json -t -s
```

**Redirect trace to file:**
```bash
dotnet nethtest.dll -b -i /path/to/blocktest.json -t 2>trace.jsonl
```

#### Blocktest Trace Output

When tracing is enabled for block tests (`-t`), traces are written to **stderr** in a consolidated stream format compatible with go-ethereum:

- All blocks and transactions in a single continuous stream
- JSON-lines format (one JSON object per line)
- Each line represents one EVM opcode execution
- Transaction summary lines with `output` and `gasUsed` fields

Example trace output:
```jsonl
{"pc":0,"op":125,"gas":"0xf3d1f8","gasCost":"0x3","memSize":0,"stack":[],"depth":1,"refund":0,"opName":"PUSH30"}
{"pc":31,"op":96,"gas":"0xf3d1f5","gasCost":"0x3","memSize":0,"stack":["0x153136..."],"depth":1,"refund":0,"opName":"PUSH1"}
{"pc":33,"op":82,"gas":"0xf3d1f2","gasCost":"0x6","memSize":0,"stack":["0x153136...","0x0"],"depth":1,"refund":0,"opName":"MSTORE"}
...
{"output":"0x","gasUsed":"0xaf8e"}
{"pc":0,"op":96,"gas":"0xf424e0","gasCost":"0x3","memSize":0,"stack":[],"depth":1,"refund":0,"opName":"PUSH1"}
...
{"output":"0x0000...0406","gasUsed":"0x0"}
{"testEnd":{"name":"test_name","pass":true,"fork":"Cancun","v":1,"d":1.234,"gasUsed":"0xaf8e","txs":2,"blocks":1,"root":"0xabcd..."}}
```

#### Test End Marker

The **last line** of the trace is a JSONL-compliant end marker containing test metadata:

```jsonl
{"testEnd":{"name":"string","pass":bool,"fork":"string","v":1,...}}
```

**Required fields:**
- `name`: Test identifier
- `pass`: Test result (true/false)
- `fork`: Network/fork name
- `v`: Format version (1)

**Optional fields:**
- `d`: Duration in seconds
- `gasUsed`: Total gas used (hex)
- `txs`: Transaction count
- `blocks`: Block count
- `root`: Final state root

**Validate trace:**
```bash
# Check test completion and result
tail -1 trace.jsonl | jq '.testEnd.pass'

# Validate trace format
./tools/validate-blocktest-trace.sh trace.jsonl
```

See [BLOCKTEST_TRACE_END_MARKER.md](../../../BLOCKTEST_TRACE_END_MARKER.md) for full specification.

**Comparing with go-ethereum:**
```bash
# Nethermind trace
dotnet nethtest.dll -b -i test.json -t 2>nethermind-trace.jsonl

# Go-ethereum trace (future: will have same end marker)
evm blocktest test.json --trace 2>geth-trace.jsonl

# Compare traces (excluding end markers)
diff \
  <(head -n -1 nethermind-trace.jsonl) \
  <(head -n -1 geth-trace.jsonl)

# Compare results
diff \
  <(jq '.testEnd | {name,pass,root}' < <(tail -1 nethermind-trace.jsonl)) \
  <(jq '.testEnd | {name,pass,root}' < <(tail -1 geth-trace.jsonl))
```

### EOF Tests

EOF (EVM Object Format) tests verify EIP-3540+ container format validation.

**Run EOF tests:**
```bash
dotnet nethtest.dll -e -i /path/to/EOFTests/
```

## Filtering Tests

Use regex patterns to run specific tests:

**Run tests matching pattern:**
```bash
dotnet nethtest.dll -i /path/to/tests/ -f "add.*"
```

**Run specific test:**
```bash
dotnet nethtest.dll -i /path/to/tests/ -f "^add02$"
```

## Interactive Mode

Read test paths from stdin (useful for scripting):

```bash
echo "/path/to/test1.json" | dotnet nethtest.dll -x
```

## Network Selection

**Mainnet (default):**
```bash
dotnet nethtest.dll -i /path/to/tests/
```

**Gnosis Chain:**
```bash
dotnet nethtest.dll -i /path/to/tests/ -g
```

## Examples

### Example 1: Run All Block Tests in Directory
```bash
dotnet nethtest.dll -b -i ~/ethereum-tests/BlockchainTests/ValidBlocks/
```

### Example 2: Run Single Block Test with Tracing
```bash
dotnet nethtest.dll -b -i ~/tests/mytest.json -t 2>trace.jsonl
```

### Example 3: Filter State Tests by Name
```bash
dotnet nethtest.dll -i ~/ethereum-tests/GeneralStateTests/ -f "^Constantinople"
```

### Example 4: Run EOF Tests
```bash
dotnet nethtest.dll -e -i ~/ethereum-tests/EOFTests/
```

### Example 5: Batch Test Execution via Stdin
```bash
find ~/tests -name "*.json" | dotnet nethtest.dll -x
```

### Example 6: Compare Traces with go-ethereum
```bash
# Generate Nethermind trace
dotnet nethtest.dll -b -i fuzzer_input.json -t 2>nethermind.jsonl

# Generate geth trace
evm blocktest fuzzer_input.json --trace 2>geth.jsonl

# Compare
wc -l *.jsonl
diff nethermind.jsonl geth.jsonl
```

## Output

### Success Output
```
TestName                                                                 PASS
```

### Failure Output (with Tracing)
```
TestName                                                                 FAIL
[Detailed trace output on stderr]
```

### Trace Output Location
- **State tests**: Stderr (on failure by default, or always with `-t`)
- **Block tests**: Stderr (when `-t` flag is used)
- Both can be redirected to files: `2>trace.jsonl`

### Test Results Summary
JSON-formatted results are written to stdout showing:
- Test name
- Pass/fail status
- Error messages (if any)
- Execution time
- State root

## Implementation Details

### Trace Format Compatibility

The blocktest tracing implementation generates traces compatible with go-ethereum's format:
- **Consolidated output**: Single continuous stream for all blocks and transactions
- **JSON-lines format**: One JSON object per line
- **Each line**: Represents one EVM opcode execution step
- **Summary lines**: Transaction output and gas usage (`{"output":"0x...","gasUsed":"0x..."}`)
- **Output stream**: Stderr (matching state test behavior)

### Architecture

- **BlockchainTestBase**: Core test execution framework with async block processing
- **BlockchainTestsRunner**: Block test runner with streaming trace support
- **BlockchainTestStreamingTracer**: Consolidated tracer writing all transactions to stderr
- **StateTestsRunner**: State test runner with conditional tracing
- **EofTestsRunner**: EOF test runner

### Tracing Implementation

For block tests, tracing is implemented via:
1. **BlockchainTestStreamingTracer**: Streams all traces to stderr in consolidated format
2. **GethLikeTxFileTracer**: Per-transaction tracer with callback for each opcode
3. **IBlockTracer interface**: Integration with blockchain processor's tracer bag

Key implementation detail: Tracer must remain registered until `BlockchainProcessor.StopAsync()` completes, as blocks are processed asynchronously.

## Performance

Use `--warmup` flag for benchmarking to enable JIT warmup:

```bash
dotnet nethtest.dll -i /path/to/test.json -wu
```

## Troubleshooting

### Common Issues

**Issue**: Tests fail to load
```
Error: Unable to load test file
```
**Solution**: Ensure JSON file is valid Ethereum test format

**Issue**: No trace output
```
Nothing on stderr
```
**Solution**: Ensure `-t` flag is specified

**Issue**: Incomplete traces (missing transactions)
```
Only first block traced
```
**Solution**: This was a bug in earlier versions. Ensure you're using the latest version where tracer removal happens after `StopAsync()`.

**Issue**: Out of memory errors
```
OutOfMemoryException
```
**Solution**: Run fewer tests concurrently or increase available memory

### Debug Output

For detailed logging:
- **Console (stdout)**: Test pass/fail status
- **Stderr**: Trace output (redirect with `2>file.jsonl`)
- Use `-t` for block test tracing
- Use `-t -m` to include memory in traces

## Development

### Adding New Test Types

1. Implement test loader strategy (e.g., `LoadBlockchainTestStrategy`)
2. Create test runner implementing appropriate interface
3. Add command-line option in `Program.cs`
4. Register runner in `Run()` method

### Extending Tracing

To add new trace formats:
1. Implement `IBlockTracer` interface
2. Write traces to stderr (or provide `TextWriter` parameter)
3. Add CLI options for format customization

## Related Documentation

- [Ethereum Test Specifications](https://ethereum-tests.readthedocs.io/)
- [Nethermind Architecture](../../../docs/architecture.md)
- [EVM Tracing](../../Nethermind.Evm/Tracing/README.md)

## Contributing

See the main [Nethermind Contributing Guide](../../../../CONTRIBUTING.md) for development guidelines.

## License

Licensed under LGPL-3.0. See [LICENSE](../../../../LICENSE) for details.
