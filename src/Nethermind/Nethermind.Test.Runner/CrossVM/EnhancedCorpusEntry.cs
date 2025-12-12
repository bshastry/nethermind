// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Nethermind.Test.Runner.CrossVM;

/// <summary>
/// Represents a corpus entry that may contain cross-VM metadata for consensus comparison.
/// </summary>
/// <remarks>
/// This class handles parsing cross-VM metadata from state test JSON files, supporting both:
/// <list type="bullet">
/// <item><description>EEST format (preferred): "_info" field inside each test object</description></item>
/// <item><description>Legacy format: "_crossvm" field at top level (for backwards compatibility)</description></item>
/// </list>
/// <para>
/// Example EEST format:
/// <code>
/// {
///   "testName": {
///     "_info": {
///       "generatedBy": "geth",
///       "traceHash": "abc123...",
///       "stateRoot": "0x...",
///       "crossvmVersion": "1.0"
///     },
///     "env": { ... },
///     "pre": { ... }
///   }
/// }
/// </code>
/// </para>
/// </remarks>
public class EnhancedCorpusEntry
{
    private static readonly ILogger Logger = new LoggerFactory().CreateLogger<EnhancedCorpusEntry>();

    /// <summary>
    /// EEST standard field name for metadata inside test objects.
    /// </summary>
    public const string INFO_FIELD = "_info";

    /// <summary>
    /// Legacy field name for top-level metadata (backwards compatibility).
    /// </summary>
    public const string LEGACY_CROSSVM_FIELD = "_crossvm";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = false
    };

    /// <summary>
    /// Gets the raw test JSON bytes.
    /// </summary>
    public byte[] TestRaw { get; }

    /// <summary>
    /// Gets the cross-VM metadata (may be null).
    /// </summary>
    public CrossVMMetadata? Metadata { get; }

    /// <summary>
    /// Gets the name of the test containing the metadata (for EEST format).
    /// </summary>
    public string? TestName { get; }

    /// <summary>
    /// Gets a value indicating whether this entry uses EEST format (_info inside test object).
    /// </summary>
    public bool UsesEestFormat { get; }

    private EnhancedCorpusEntry(byte[] testRaw, CrossVMMetadata? metadata, string? testName, bool usesEestFormat)
    {
        TestRaw = testRaw;
        Metadata = metadata;
        TestName = testName;
        UsesEestFormat = usesEestFormat;
    }

    /// <summary>
    /// Parses a corpus entry from JSON bytes, extracting any cross-VM metadata.
    /// </summary>
    /// <param name="json">The raw JSON bytes</param>
    /// <returns>The enhanced corpus entry</returns>
    /// <remarks>
    /// Supports both EEST format (_info inside test) and legacy format (_crossvm at top level).
    /// </remarks>
    public static EnhancedCorpusEntry Parse(byte[] json)
    {
        if (json is null || json.Length == 0)
        {
            return new EnhancedCorpusEntry(json ?? [], null, null, false);
        }

        try
        {
            JsonNode? root = JsonNode.Parse(json);
            if (root is not JsonObject rootObj)
            {
                return new EnhancedCorpusEntry(json, null, null, false);
            }

            CrossVMMetadata? metadata = null;
            string? testName = null;
            bool usesEestFormat = false;

            // First, try EEST format: _info inside test objects
            foreach (var kvp in rootObj)
            {
                string fieldName = kvp.Key;

                // Skip the legacy _crossvm field
                if (fieldName == LEGACY_CROSSVM_FIELD)
                {
                    continue;
                }

                JsonNode? testNode = kvp.Value;
                if (testNode is JsonObject testObj && testObj.ContainsKey(INFO_FIELD))
                {
                    JsonNode? infoNode = testObj[INFO_FIELD];
                    if (infoNode is JsonObject infoObj)
                    {
                        // Check if this _info has cross-VM metadata (has generatedBy field)
                        if (infoObj.ContainsKey("generatedBy") || infoObj.ContainsKey("traceHash"))
                        {
                            metadata = JsonSerializer.Deserialize<CrossVMMetadata>(infoNode.ToJsonString(), JsonOptions);
                            testName = fieldName;
                            usesEestFormat = true;
                            break; // Use metadata from first test found
                        }
                    }
                }
            }

            // Fallback to legacy format: _crossvm at top level
            if (metadata is null && rootObj.ContainsKey(LEGACY_CROSSVM_FIELD))
            {
                JsonNode? crossvmNode = rootObj[LEGACY_CROSSVM_FIELD];
                if (crossvmNode is not null)
                {
                    metadata = JsonSerializer.Deserialize<CrossVMMetadata>(crossvmNode.ToJsonString(), JsonOptions);
                    usesEestFormat = false;
                }
            }

            return new EnhancedCorpusEntry(json, metadata, testName, usesEestFormat);
        }
        catch (Exception e)
        {
            Logger.LogDebug(e, "Failed to parse corpus entry for cross-VM metadata");
            return new EnhancedCorpusEntry(json, null, null, false);
        }
    }

    /// <summary>
    /// Checks if this entry has cross-VM metadata from another client.
    /// </summary>
    /// <param name="thisClient">The current client name (e.g., "nethermind")</param>
    /// <returns>True if metadata exists and is from a different client</returns>
    public bool ShouldVerifyAgainst(string thisClient) =>
        Metadata is not null && Metadata.HasTraceHash() && Metadata.IsFromOtherClient(thisClient);

    /// <summary>
    /// Checks if this entry has any cross-VM metadata.
    /// </summary>
    /// <returns>True if metadata is present</returns>
    public bool HasMetadata() => Metadata is not null;

    /// <summary>
    /// Returns the test JSON with cross-VM metadata fields removed.
    /// </summary>
    /// <returns>The JSON bytes suitable for test execution</returns>
    /// <remarks>
    /// This removes both:
    /// <list type="bullet">
    /// <item><description>Legacy _crossvm field at top level</description></item>
    /// <item><description>EEST _info field inside test objects (only cross-VM fields, preserving standard _info)</description></item>
    /// </list>
    /// Note: For EEST format, we don't remove the entire _info field since it may contain standard EEST metadata.
    /// The test executor ignores unknown fields anyway.
    /// </remarks>
    public byte[] GetTestWithoutMetadata()
    {
        if (Metadata is null)
        {
            return TestRaw; // No metadata to remove
        }

        try
        {
            JsonNode? root = JsonNode.Parse(TestRaw);
            if (root is not JsonObject rootObj)
            {
                return TestRaw;
            }

            bool modified = false;

            // Remove legacy _crossvm field if present
            if (rootObj.ContainsKey(LEGACY_CROSSVM_FIELD))
            {
                rootObj.Remove(LEGACY_CROSSVM_FIELD);
                modified = true;
            }

            // For EEST format, the _info field is inside test objects.
            // We don't remove it since Nethermind's parser ignores unknown fields.
            // However, if strict compatibility is needed, we could strip cross-VM specific fields.

            if (modified)
            {
                return JsonSerializer.SerializeToUtf8Bytes(root, JsonOptions);
            }

            return TestRaw;
        }
        catch (Exception e)
        {
            Logger.LogDebug(e, "Failed to strip cross-VM metadata");
            return TestRaw;
        }
    }

    /// <summary>
    /// Gets the trace hash from metadata (if present).
    /// </summary>
    /// <returns>The trace hash or null</returns>
    public string? GetTraceHash() => Metadata?.TraceHash;

    /// <summary>
    /// Gets the generating client name from metadata.
    /// </summary>
    /// <returns>The client name (e.g., "geth") or null</returns>
    public string? GetGeneratedBy() => Metadata?.GeneratedBy;

    /// <summary>
    /// Gets the fork name from metadata (if present).
    /// </summary>
    /// <returns>The fork name (e.g., "Prague") or null</returns>
    public string? GetFork() => Metadata?.Fork;
}
