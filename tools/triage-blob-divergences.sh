#!/bin/bash

# =============================================================================
# triage-blob-divergences.sh
# =============================================================================
# Parallel blob divergence triage script for cross-VM consensus validation.
#
# This script processes divergent test files from fuzzing campaigns through
# multiple EVM implementations (geth, nethermind, besu) to identify genuine
# consensus issues.
#
# USAGE:
#   ./triage-blob-divergences.sh [OPTIONS]
#
# OPTIONS:
#   --dry-run           Show what would be executed without running
#   --parallel N        Number of parallel workers (default: 160)
#   --batch-size N      Files per batch (default: 1000)
#   --skip-trace        Only compare stateroots, skip trace comparison
#   --help              Show this help message
#
# ENVIRONMENT VARIABLES:
#   FEEDBACK_DIR        Directory containing divergent test files
#                       (default: /dev/shm/goevmlab-hybrid-277037/feedback)
#   GETH_BIN            Path to geth evm binary
#                       (default: ../go-ethereum/build/bin/evm)
#   NETH_BIN            Path to nethermind nethtest binary
#                       (default: ../nethermind/src/Nethermind/artifacts/bin/Nethermind.Test.Runner/release/nethtest)
#   BESU_BIN            Path to besu evmtool binary
#                       (default: ../besu/ethereum/evmtool/build/install/evmtool/bin/evmtool)
#   GOEVMLAB_DIR        Path to goevmlab repository
#                       (default: ../goevmlab)
#   OUTPUT_BASE         Base directory for output
#                       (default: /tmp/blob-triage)
#   ORPHAN_TIMEOUT      Timeout for orphaned VMs (default: 30s)
#
# EXAMPLES:
#   # Dry run to see what would happen
#   ./triage-blob-divergences.sh --dry-run
#
#   # Run with custom parallelism
#   ./triage-blob-divergences.sh --parallel 100
#
#   # Fast mode (skip trace comparison)
#   ./triage-blob-divergences.sh --skip-trace
#
#   # Custom paths
#   FEEDBACK_DIR=/path/to/divergences ./triage-blob-divergences.sh
#
# =============================================================================

set -euo pipefail

# -----------------------------------------------------------------------------
# Configuration
# -----------------------------------------------------------------------------

# Terminal colors (disabled if not a terminal)
if [[ -t 1 ]]; then
    RED='\033[0;31m'
    GREEN='\033[0;32m'
    YELLOW='\033[1;33m'
    BLUE='\033[0;34m'
    CYAN='\033[0;36m'
    BOLD='\033[1m'
    NC='\033[0m'
else
    RED=''
    GREEN=''
    YELLOW=''
    BLUE=''
    CYAN=''
    BOLD=''
    NC=''
fi

# Script directory for relative path resolution
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Default configuration with environment variable overrides
FEEDBACK_DIR="${FEEDBACK_DIR:-/dev/shm/goevmlab-hybrid-277037/feedback}"
GETH_BIN="${GETH_BIN:-$REPO_ROOT/../go-ethereum/build/bin/evm}"
NETH_BIN="${NETH_BIN:-$REPO_ROOT/src/Nethermind/artifacts/bin/Nethermind.Test.Runner/release/nethtest}"
BESU_BIN="${BESU_BIN:-$REPO_ROOT/../besu/ethereum/evmtool/build/install/evmtool/bin/evmtool}"
GOEVMLAB_DIR="${GOEVMLAB_DIR:-$REPO_ROOT/../goevmlab}"
OUTPUT_BASE="${OUTPUT_BASE:-/tmp/blob-triage}"
ORPHAN_TIMEOUT="${ORPHAN_TIMEOUT:-30s}"

# Runtime defaults
PARALLEL_WORKERS=160
BATCH_SIZE=1000
DRY_RUN=false
SKIP_TRACE=false

# Derived paths
RUNTEST_BIN=""
TIMESTAMP=""
OUTPUT_DIR=""
LOG_FILE=""
SUMMARY_FILE=""
FILE_LIST=""

# Statistics
TOTAL_FILES=0
START_TIME=0

# -----------------------------------------------------------------------------
# Utility Functions
# -----------------------------------------------------------------------------

usage() {
    cat << 'EOF'
USAGE:
  ./triage-blob-divergences.sh [OPTIONS]

OPTIONS:
  --dry-run           Show what would be executed without running
  --parallel N        Number of parallel workers (default: 160)
  --batch-size N      Files per batch (default: 1000)
  --skip-trace        Only compare stateroots, skip trace comparison
  --help              Show this help message

ENVIRONMENT VARIABLES:
  FEEDBACK_DIR        Directory containing divergent test files
                      (default: /dev/shm/goevmlab-hybrid-277037/feedback)
  GETH_BIN            Path to geth evm binary
                      (default: ../go-ethereum/build/bin/evm)
  NETH_BIN            Path to nethermind nethtest binary
                      (default: ../nethermind/src/Nethermind/artifacts/bin/Nethermind.Test.Runner/release/nethtest)
  BESU_BIN            Path to besu evmtool binary
                      (default: ../besu/ethereum/evmtool/build/install/evmtool/bin/evmtool)
  GOEVMLAB_DIR        Path to goevmlab repository
                      (default: ../goevmlab)
  OUTPUT_BASE         Base directory for output
                      (default: /tmp/blob-triage)
  ORPHAN_TIMEOUT      Timeout for orphaned VMs (default: 30s)

EXAMPLES:
  # Dry run to see what would happen
  ./triage-blob-divergences.sh --dry-run

  # Run with custom parallelism
  ./triage-blob-divergences.sh --parallel 100

  # Fast mode (skip trace comparison)
  ./triage-blob-divergences.sh --skip-trace

  # Custom paths
  FEEDBACK_DIR=/path/to/divergences ./triage-blob-divergences.sh
EOF
    exit 0
}

log() {
    local level="$1"
    shift
    local timestamp
    timestamp=$(date '+%Y-%m-%d %H:%M:%S')

    case "$level" in
        INFO)  echo -e "${BLUE}[$timestamp]${NC} ${GREEN}INFO${NC}  $*" ;;
        WARN)  echo -e "${BLUE}[$timestamp]${NC} ${YELLOW}WARN${NC}  $*" ;;
        ERROR) echo -e "${BLUE}[$timestamp]${NC} ${RED}ERROR${NC} $*" ;;
        DEBUG) echo -e "${BLUE}[$timestamp]${NC} ${CYAN}DEBUG${NC} $*" ;;
        *)     echo -e "${BLUE}[$timestamp]${NC} $*" ;;
    esac
}

die() {
    log ERROR "$@"
    exit 1
}

check_binary() {
    local name="$1"
    local path="$2"

    if [[ ! -x "$path" ]]; then
        if [[ -f "$path" ]]; then
            die "Binary '$name' exists but is not executable: $path"
        else
            die "Binary '$name' not found: $path"
        fi
    fi
    log INFO "Found $name: $path"
}

format_duration() {
    local seconds="$1"
    local hours=$((seconds / 3600))
    local minutes=$(((seconds % 3600) / 60))
    local secs=$((seconds % 60))

    if [[ $hours -gt 0 ]]; then
        printf "%dh %dm %ds" "$hours" "$minutes" "$secs"
    elif [[ $minutes -gt 0 ]]; then
        printf "%dm %ds" "$minutes" "$secs"
    else
        printf "%ds" "$secs"
    fi
}

format_number() {
    printf "%'d" "$1" 2>/dev/null || echo "$1"
}

estimate_time() {
    local file_count="$1"
    local workers="$2"

    # Empirical estimate: ~0.5 seconds per test per VM with batch mode
    # With 3 VMs and parallelism, effective throughput is roughly:
    # tests_per_second = workers * 0.8 (accounting for coordination overhead)
    local tests_per_second
    tests_per_second=$(echo "scale=2; $workers * 0.8" | bc)

    local estimated_seconds
    estimated_seconds=$(echo "scale=0; $file_count / $tests_per_second" | bc)

    format_duration "$estimated_seconds"
}

# -----------------------------------------------------------------------------
# Setup Functions
# -----------------------------------------------------------------------------

parse_args() {
    while [[ $# -gt 0 ]]; do
        case "$1" in
            --dry-run)
                DRY_RUN=true
                shift
                ;;
            --parallel)
                PARALLEL_WORKERS="$2"
                shift 2
                ;;
            --batch-size)
                BATCH_SIZE="$2"
                shift 2
                ;;
            --skip-trace)
                SKIP_TRACE=true
                shift
                ;;
            --help|-h)
                usage
                ;;
            *)
                die "Unknown option: $1 (use --help for usage)"
                ;;
        esac
    done
}

validate_environment() {
    log INFO "Validating environment..."

    # Check feedback directory
    if [[ ! -d "$FEEDBACK_DIR" ]]; then
        die "Feedback directory not found: $FEEDBACK_DIR"
    fi
    log INFO "Feedback directory: $FEEDBACK_DIR"

    # Check EVM binaries
    check_binary "geth" "$GETH_BIN"
    check_binary "nethermind" "$NETH_BIN"
    check_binary "besu" "$BESU_BIN"

    # Check/build runtest
    RUNTEST_BIN="$GOEVMLAB_DIR/cmd/runtest/runtest"
    if [[ ! -x "$RUNTEST_BIN" ]]; then
        log INFO "Building runtest binary..."
        if [[ ! -d "$GOEVMLAB_DIR" ]]; then
            die "goevmlab directory not found: $GOEVMLAB_DIR"
        fi
        (
            cd "$GOEVMLAB_DIR/cmd/runtest"
            go build -o runtest . || die "Failed to build runtest"
        )
        if [[ ! -x "$RUNTEST_BIN" ]]; then
            die "Failed to create runtest binary at: $RUNTEST_BIN"
        fi
    fi
    log INFO "Using runtest: $RUNTEST_BIN"

    # Check bc for calculations
    if ! command -v bc &>/dev/null; then
        die "Required tool 'bc' not found. Install with: apt install bc"
    fi

    # Check available system resources
    local cpus
    cpus=$(nproc)
    local mem_gb
    mem_gb=$(free -g | awk '/^Mem:/{print $2}')

    log INFO "System resources: ${cpus} CPUs, ${mem_gb}GB RAM"

    if [[ $PARALLEL_WORKERS -gt $((cpus * 2)) ]]; then
        log WARN "Parallel workers ($PARALLEL_WORKERS) exceeds 2x CPU count ($cpus)"
        log WARN "Consider reducing --parallel for better performance"
    fi
}

setup_output_directory() {
    TIMESTAMP=$(date '+%Y%m%d-%H%M%S')
    OUTPUT_DIR="$OUTPUT_BASE/triage-$TIMESTAMP"
    LOG_FILE="$OUTPUT_DIR/triage.log"
    SUMMARY_FILE="$OUTPUT_DIR/summary.txt"
    FILE_LIST="$OUTPUT_DIR/file-list.txt"

    mkdir -p "$OUTPUT_DIR"

    log INFO "Output directory: $OUTPUT_DIR"
}

collect_divergent_files() {
    log INFO "Scanning for divergent test files..."

    # Find all div-*.json files (both with and without 'feedback' in name)
    # The pattern div-*-*.json matches the divergence output format
    find "$FEEDBACK_DIR" -maxdepth 1 -name 'div-*.json' -type f > "$FILE_LIST" 2>/dev/null || true

    TOTAL_FILES=$(wc -l < "$FILE_LIST" | tr -d ' ')

    if [[ $TOTAL_FILES -eq 0 ]]; then
        die "No divergent test files found matching pattern 'div-*.json' in $FEEDBACK_DIR"
    fi

    log INFO "Found $(format_number $TOTAL_FILES) divergent test files"

    # Show breakdown by type
    local feedback_count non_feedback_count
    feedback_count=$(grep -c 'feedback' "$FILE_LIST" || echo 0)
    non_feedback_count=$((TOTAL_FILES - feedback_count))

    log INFO "  - With 'feedback' mutation: $(format_number $feedback_count)"
    log INFO "  - Original divergences: $(format_number $non_feedback_count)"
}

# -----------------------------------------------------------------------------
# Execution Functions
# -----------------------------------------------------------------------------

print_configuration() {
    echo ""
    echo -e "${BOLD}========================================${NC}"
    echo -e "${BOLD}  Blob Divergence Triage Configuration${NC}"
    echo -e "${BOLD}========================================${NC}"
    echo ""
    echo -e "  ${CYAN}Input:${NC}"
    echo -e "    Feedback Directory : $FEEDBACK_DIR"
    echo -e "    Total Files        : $(format_number $TOTAL_FILES)"
    echo ""
    echo -e "  ${CYAN}Execution:${NC}"
    echo -e "    Parallel Workers   : $PARALLEL_WORKERS"
    echo -e "    Batch Size         : $BATCH_SIZE"
    echo -e "    Skip Trace         : $SKIP_TRACE"
    echo -e "    Orphan Timeout     : $ORPHAN_TIMEOUT"
    echo ""
    echo -e "  ${CYAN}EVM Binaries:${NC}"
    echo -e "    Geth               : $GETH_BIN"
    echo -e "    Nethermind         : $NETH_BIN"
    echo -e "    Besu               : $BESU_BIN"
    echo ""
    echo -e "  ${CYAN}Output:${NC}"
    echo -e "    Output Directory   : $OUTPUT_DIR"
    echo -e "    Log File           : $LOG_FILE"
    echo ""
    echo -e "  ${CYAN}Estimated Time:${NC}"
    echo -e "    $(estimate_time $TOTAL_FILES $PARALLEL_WORKERS) (rough estimate)"
    echo ""
    echo -e "${BOLD}========================================${NC}"
    echo ""
}

run_triage_batch() {
    local batch_num="$1"
    local batch_file="$2"
    local batch_outdir="$OUTPUT_DIR/batch-$batch_num"
    local batch_count
    batch_count=$(wc -l < "$batch_file" | tr -d ' ')

    mkdir -p "$batch_outdir"

    log INFO "Starting batch $batch_num: $(format_number $batch_count) files"

    # Build runtest command
    local cmd=(
        "$RUNTEST_BIN"
        --gethbatch "$GETH_BIN"
        --nethbatch "$NETH_BIN"
        --besubatch "$BESU_BIN"
        --parallel "$PARALLEL_WORKERS"
        --outdir "$batch_outdir"
        --orphan-timeout "$ORPHAN_TIMEOUT"
        --verbosity 0
    )

    if [[ "$SKIP_TRACE" == "true" ]]; then
        cmd+=(--skiptrace)
    fi

    # runtest accepts a glob pattern, so we'll create a temp file with all paths
    # and use it to generate a pattern that includes all files
    local pattern_file="$batch_outdir/patterns.txt"

    # For large batches, we need to pass files as individual arguments
    # runtest uses filepath.Glob, so we need to be creative
    # Best approach: use a shell glob that expands to all files in the batch

    # Copy batch files to a temp processing directory
    local batch_staging="$batch_outdir/staging"
    mkdir -p "$batch_staging"

    while IFS= read -r file; do
        cp "$file" "$batch_staging/"
    done < "$batch_file"

    # Now we can use a simple glob
    cmd+=("$batch_staging/*.json")

    log INFO "Executing: ${cmd[*]:0:10}... (truncated)"

    if [[ "$DRY_RUN" == "true" ]]; then
        log INFO "[DRY RUN] Would execute: ${cmd[*]}"
        return 0
    fi

    # Execute with output capturing
    local batch_log="$batch_outdir/execution.log"
    local batch_start
    batch_start=$(date +%s)

    if "${cmd[@]}" > "$batch_log" 2>&1; then
        local batch_end
        batch_end=$(date +%s)
        local duration=$((batch_end - batch_start))
        log INFO "Batch $batch_num completed in $(format_duration $duration)"
    else
        local exit_code=$?
        log WARN "Batch $batch_num exited with code $exit_code (may indicate divergences found)"
    fi

    # Clean up staging directory to save space
    rm -rf "$batch_staging"

    # Count results
    local divergence_count=0
    if [[ -d "$batch_outdir" ]]; then
        divergence_count=$(find "$batch_outdir" -name '*.json' -type f 2>/dev/null | wc -l | tr -d ' ')
    fi

    if [[ $divergence_count -gt 0 ]]; then
        log INFO "Batch $batch_num: Found $divergence_count potential divergences"
    fi
}

run_triage() {
    log INFO "Starting divergence triage..."
    START_TIME=$(date +%s)

    # Split files into batches
    local batch_dir="$OUTPUT_DIR/batches"
    mkdir -p "$batch_dir"

    split -l "$BATCH_SIZE" -d -a 4 "$FILE_LIST" "$batch_dir/batch-"

    local batch_count
    batch_count=$(find "$batch_dir" -name 'batch-*' -type f | wc -l | tr -d ' ')

    log INFO "Split into $batch_count batches of up to $BATCH_SIZE files each"

    # Process batches sequentially (runtest handles parallelism internally)
    local batch_num=0
    for batch_file in "$batch_dir"/batch-*; do
        batch_num=$((batch_num + 1))
        run_triage_batch "$batch_num" "$batch_file"

        # Progress update
        local elapsed=$(($(date +%s) - START_TIME))
        local pct=$((batch_num * 100 / batch_count))
        log INFO "Progress: $batch_num/$batch_count batches ($pct%) - elapsed: $(format_duration $elapsed)"
    done
}

generate_summary() {
    local end_time
    end_time=$(date +%s)
    local total_duration=$((end_time - START_TIME))

    log INFO "Generating summary report..."

    # Count results
    local total_divergences=0
    local divergence_files=()

    while IFS= read -r -d '' file; do
        divergence_files+=("$file")
        total_divergences=$((total_divergences + 1))
    done < <(find "$OUTPUT_DIR" -path '*/batch-*/div-*.json' -type f -print0 2>/dev/null)

    # Generate summary
    {
        echo "=========================================="
        echo "  Blob Divergence Triage Summary"
        echo "=========================================="
        echo ""
        echo "Timestamp: $(date)"
        echo "Duration: $(format_duration $total_duration)"
        echo ""
        echo "Input:"
        echo "  Feedback Directory: $FEEDBACK_DIR"
        echo "  Total Files Processed: $(format_number $TOTAL_FILES)"
        echo ""
        echo "Configuration:"
        echo "  Parallel Workers: $PARALLEL_WORKERS"
        echo "  Batch Size: $BATCH_SIZE"
        echo "  Skip Trace: $SKIP_TRACE"
        echo ""
        echo "Results:"
        echo "  Total Divergences Found: $(format_number $total_divergences)"
        echo "  Divergence Rate: $(echo "scale=2; $total_divergences * 100 / $TOTAL_FILES" | bc)%"
        echo ""
        echo "Performance:"
        echo "  Tests/Second: $(echo "scale=2; $TOTAL_FILES / $total_duration" | bc)"
        echo ""
        echo "Output:"
        echo "  Results Directory: $OUTPUT_DIR"
        echo ""

        if [[ $total_divergences -gt 0 ]]; then
            echo "Divergence Files:"
            echo "-----------------"
            printf '%s\n' "${divergence_files[@]}" | head -50
            if [[ $total_divergences -gt 50 ]]; then
                echo "... and $((total_divergences - 50)) more"
            fi
        fi
    } > "$SUMMARY_FILE"

    # Also output to console
    cat "$SUMMARY_FILE"

    log INFO "Summary saved to: $SUMMARY_FILE"
}

cleanup() {
    local exit_code=$?

    if [[ $exit_code -ne 0 ]] && [[ "$DRY_RUN" != "true" ]]; then
        log WARN "Script interrupted or failed with exit code: $exit_code"
        log INFO "Partial results may be available in: $OUTPUT_DIR"
    fi

    # Clean up batch staging directories if they exist
    if [[ -d "$OUTPUT_DIR" ]]; then
        find "$OUTPUT_DIR" -type d -name 'staging' -exec rm -rf {} + 2>/dev/null || true
    fi
}

# -----------------------------------------------------------------------------
# Main
# -----------------------------------------------------------------------------

main() {
    trap cleanup EXIT

    echo ""
    echo -e "${BOLD}Blob Divergence Triage Script${NC}"
    echo -e "${CYAN}Cross-VM Consensus Validation Tool${NC}"
    echo ""

    parse_args "$@"
    validate_environment
    setup_output_directory
    collect_divergent_files
    print_configuration

    if [[ "$DRY_RUN" == "true" ]]; then
        log INFO "Dry run mode - no actual execution will occur"
        echo ""

        # Show sample command
        echo -e "${YELLOW}Sample command that would be executed:${NC}"
        echo ""
        echo "  $RUNTEST_BIN \\"
        echo "    --gethbatch $GETH_BIN \\"
        echo "    --nethbatch $NETH_BIN \\"
        echo "    --besubatch $BESU_BIN \\"
        echo "    --parallel $PARALLEL_WORKERS \\"
        echo "    --outdir $OUTPUT_DIR/batch-N \\"
        echo "    --orphan-timeout $ORPHAN_TIMEOUT \\"
        if [[ "$SKIP_TRACE" == "true" ]]; then
            echo "    --skiptrace \\"
        fi
        echo "    '<batch-staging-dir>/*.json'"
        echo ""

        log INFO "Dry run complete. Remove --dry-run to execute."
        exit 0
    fi

    # Confirmation for large runs
    if [[ $TOTAL_FILES -gt 10000 ]]; then
        echo -e "${YELLOW}Warning: About to process $(format_number $TOTAL_FILES) files.${NC}"
        echo -e "${YELLOW}Estimated time: $(estimate_time $TOTAL_FILES $PARALLEL_WORKERS)${NC}"
        echo ""
        read -p "Continue? [y/N] " -n 1 -r
        echo ""
        if [[ ! $REPLY =~ ^[Yy]$ ]]; then
            log INFO "Aborted by user"
            exit 0
        fi
    fi

    # Start logging to file
    exec > >(tee -a "$LOG_FILE") 2>&1

    run_triage
    generate_summary

    log INFO "Triage complete!"
    echo ""
    echo -e "${GREEN}Results saved to: $OUTPUT_DIR${NC}"
}

main "$@"
