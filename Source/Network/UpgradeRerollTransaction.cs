using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace MoreStandsForShops.Network;

internal readonly struct UpgradeRerollTransactionEntry
{
    internal readonly int OriginalViewId;
    internal readonly string ResourcePath;
    internal readonly Vector3 Position;
    internal readonly Quaternion Rotation;

    internal UpgradeRerollTransactionEntry(
        int originalViewId,
        string resourcePath,
        Vector3 position,
        Quaternion rotation)
    {
        OriginalViewId = originalViewId;
        ResourcePath = resourcePath ?? string.Empty;
        Position = position;
        Rotation = rotation;
    }
}

/// <summary>
/// Encodes a pending reroll into Photon-supported primitive room-property data.
/// The plan is written before the animation mutates any room object, allowing a
/// replacement master client to finish the exact same operation.
/// </summary>
internal static class UpgradeRerollTransactionCodec
{
    private const char FieldSeparator = '|';

    internal static string Encode(IEnumerable<UpgradeRerollTransactionEntry> entries)
    {
        if (entries == null)
            return string.Empty;

        var builder = new StringBuilder();
        foreach (UpgradeRerollTransactionEntry entry in entries)
        {
            if (builder.Length > 0)
                builder.Append('\n');

            builder.Append(entry.OriginalViewId.ToString(CultureInfo.InvariantCulture));
            builder.Append(FieldSeparator);
            builder.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(entry.ResourcePath)));
            AppendFloat(builder, entry.Position.x);
            AppendFloat(builder, entry.Position.y);
            AppendFloat(builder, entry.Position.z);
            AppendFloat(builder, entry.Rotation.x);
            AppendFloat(builder, entry.Rotation.y);
            AppendFloat(builder, entry.Rotation.z);
            AppendFloat(builder, entry.Rotation.w);
        }

        return builder.ToString();
    }

    internal static bool TryDecode(string encoded, out List<UpgradeRerollTransactionEntry> entries)
    {
        entries = new List<UpgradeRerollTransactionEntry>();
        if (string.IsNullOrWhiteSpace(encoded))
            return true;

        string[] records = encoded.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string record in records)
        {
            string[] fields = record.Split(FieldSeparator);
            if (fields.Length != 9 ||
                !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int viewId) ||
                !TryParseFloat(fields[2], out float px) ||
                !TryParseFloat(fields[3], out float py) ||
                !TryParseFloat(fields[4], out float pz) ||
                !TryParseFloat(fields[5], out float rx) ||
                !TryParseFloat(fields[6], out float ry) ||
                !TryParseFloat(fields[7], out float rz) ||
                !TryParseFloat(fields[8], out float rw))
            {
                entries.Clear();
                return false;
            }

            string resourcePath;
            try
            {
                resourcePath = Encoding.UTF8.GetString(Convert.FromBase64String(fields[1]));
            }
            catch (FormatException)
            {
                entries.Clear();
                return false;
            }

            if (string.IsNullOrWhiteSpace(resourcePath))
            {
                entries.Clear();
                return false;
            }

            entries.Add(new UpgradeRerollTransactionEntry(
                viewId,
                resourcePath,
                new Vector3(px, py, pz),
                new Quaternion(rx, ry, rz, rw)));
        }

        return true;
    }

    private static void AppendFloat(StringBuilder builder, float value)
    {
        builder.Append(FieldSeparator);
        builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
    }

    private static bool TryParseFloat(string value, out float parsed)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) &&
               !float.IsNaN(parsed) &&
               !float.IsInfinity(parsed);
    }
}
