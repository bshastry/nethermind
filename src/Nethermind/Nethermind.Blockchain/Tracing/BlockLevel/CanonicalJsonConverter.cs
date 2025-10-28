// SPDX-FileCopyrightText: 2025 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nethermind.Core.Extensions;

namespace Nethermind.Blockchain.Tracing.BlockLevel;

/// <summary>
/// JSON converter that ensures canonical key ordering for block-level traces.
/// Implements the EIP canonical format specification:
/// - "type" field MUST appear first
/// - Primary identifier (blockNumber, txIndex, operation) MUST appear second
/// - All other fields MUST appear in alphabetical order
/// </summary>
public class CanonicalJsonConverter : JsonConverter<object>
{
    /// <summary>
    /// Primary identifier fields by record type.
    /// </summary>
    private static readonly Dictionary<string, string> PrimaryIdentifiers = new()
    {
        ["blockStart"] = "blockNumber",
        ["blockEnd"] = "blockNumber",
        ["preExecution"] = "operation",
        ["postExecution"] = "operation",
        ["txStart"] = "txIndex",
        ["txEnd"] = "txIndex",
        ["validation"] = "operation",
        ["trieOperation"] = "operation"
    };

    public override bool CanConvert(Type typeToConvert)
    {
        return true; // Handle all object types
    }

    public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Reading is not implemented - this converter is write-only for trace generation
        throw new NotImplementedException("CanonicalJsonConverter is write-only");
    }

    public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        // Serialize to JsonElement first to extract properties
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            // Not an object, write as-is
            root.WriteTo(writer);
            return;
        }

        writer.WriteStartObject();

        // Extract all properties
        var properties = root.EnumerateObject().ToList();

        // Find "type" field and write it first
        JsonProperty? typeProperty = null;
        foreach (var prop in properties)
        {
            if (prop.Name == "type")
            {
                typeProperty = prop;
                break;
            }
        }

        string? typeFieldName = null;
        string? primaryFieldName = null;

        if (typeProperty.HasValue)
        {
            writer.WritePropertyName("type");
            WriteValue(writer, typeProperty.Value.Value, options);
            typeFieldName = "type";

            // Determine and write primary identifier second
            if (typeProperty.Value.Value.ValueKind == JsonValueKind.String)
            {
                var typeValue = typeProperty.Value.Value.GetString();
                if (typeValue != null && PrimaryIdentifiers.TryGetValue(typeValue, out var primaryId))
                {
                    // Find primary identifier field
                    foreach (var prop in properties)
                    {
                        if (prop.Name == primaryId)
                        {
                            writer.WritePropertyName(primaryId);
                            WriteValue(writer, prop.Value, options);
                            primaryFieldName = primaryId;
                            break;
                        }
                    }
                }
            }
        }

        // Write remaining properties in alphabetical order (excluding type and primary identifier)
        var remainingProperties = properties
            .Where(p => p.Name != typeFieldName && p.Name != primaryFieldName)
            .OrderBy(p => p.Name);

        foreach (var property in remainingProperties)
        {
            writer.WritePropertyName(property.Name);
            WriteValue(writer, property.Value, options);
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Recursively writes a JSON value with canonical ordering for nested objects.
    /// </summary>
    private void WriteValue(Utf8JsonWriter writer, JsonElement element, JsonSerializerOptions options)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();

                // Recursively apply canonical ordering to nested objects
                var properties = element.EnumerateObject()
                    .OrderBy(p => p.Name); // All nested object properties are alphabetical

                foreach (var property in properties)
                {
                    writer.WritePropertyName(property.Name);
                    WriteValue(writer, property.Value, options);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteValue(writer, item, options);
                }
                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;

            case JsonValueKind.Number:
                if (element.TryGetInt32(out var intValue))
                {
                    writer.WriteNumberValue(intValue);
                }
                else if (element.TryGetInt64(out var longValue))
                {
                    writer.WriteNumberValue(longValue);
                }
                else if (element.TryGetDouble(out var doubleValue))
                {
                    writer.WriteNumberValue(doubleValue);
                }
                break;

            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;

            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;

            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
        }
    }
}

/// <summary>
/// Helper methods for generating canonical format traces.
/// </summary>
public static class CanonicalFormatHelpers
{
    /// <summary>
    /// Converts a long value to canonical lowercase hex format.
    /// </summary>
    public static string ToCanonicalHex(long value)
    {
        if (value < 0)
            throw new ArgumentException("Cannot convert negative value to hex", nameof(value));
        return $"0x{value:x}";
    }

    /// <summary>
    /// Converts a ulong value to canonical lowercase hex format.
    /// </summary>
    public static string ToCanonicalHex(ulong value)
    {
        return $"0x{value:x}";
    }

    /// <summary>
    /// Converts a byte value to canonical lowercase hex format.
    /// </summary>
    public static string ToCanonicalHex(byte value)
    {
        return $"0x{value:x}";
    }

    /// <summary>
    /// Converts a Nethermind address to canonical lowercase format (no EIP-55 checksum).
    /// </summary>
    public static string ToCanonicalAddress(Core.Address address)
    {
        if (address is null)
            return string.Empty;

        // ToString(true, false) = no checksum, with 0x prefix
        // Then ensure lowercase
        return address.ToString(true, false).ToLowerInvariant();
    }

    /// <summary>
    /// Converts a Nethermind hash to canonical lowercase format.
    /// </summary>
    public static string ToCanonicalHash(Core.Crypto.Hash256 hash)
    {
        if (hash is null)
            return string.Empty;

        return hash.ToString().ToLowerInvariant();
    }

    /// <summary>
    /// Converts a UInt256 value to canonical lowercase hex format.
    /// </summary>
    public static string ToCanonicalHex(Int256.UInt256 value)
    {
        return value.ToHexString(true);
    }

    /// <summary>
    /// Converts a UInt256 value representing hash bytes (32 bytes) to canonical lowercase hex format with full 32-byte padding.
    /// Storage values are always padded to 32 bytes (64 hex characters) to match geth's format and the canonical specification.
    /// </summary>
    public static string UInt256ToHashHex(Int256.UInt256 value)
    {
        // Convert UInt256 back to 32-byte array and format as hex string with explicit padding
        Span<byte> bytes = stackalloc byte[32];
        value.ToBigEndian(bytes);
        // Use noLeadingZeros: false to ensure full 32-byte padding
        return bytes.ToArray().ToHexString(withZeroX: true, noLeadingZeros: false, withEip55Checksum: false);
    }

    /// <summary>
    /// Creates JSON serializer options configured for canonical format output.
    /// </summary>
    public static JsonSerializerOptions CreateCanonicalOptions()
    {
        var options = new JsonSerializerOptions
        {
            // Compact JSON (no indentation)
            WriteIndented = false,

            // Omit null optional fields
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

            // Use camelCase for consistency
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

            // Add canonical key ordering converter
            Converters = { new CanonicalJsonConverter() }
        };

        return options;
    }

    /// <summary>
    /// Validates that a string is in canonical hex format (lowercase with 0x prefix).
    /// </summary>
    public static bool IsCanonicalHex(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        if (!value.StartsWith("0x"))
            return false;

        // Check that all hex characters are lowercase
        for (int i = 2; i < value.Length; i++)
        {
            char c = value[i];
            if (c >= 'A' && c <= 'F')
                return false; // Uppercase hex digit
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                return false; // Not a hex digit
        }

        return true;
    }
}
