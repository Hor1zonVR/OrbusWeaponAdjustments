using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace OrbusWeaponAdjustments;

/// <summary>
/// OW1 is a self-contained OFFLINE, exact-float share code. It never includes
/// PlayerPrefs key strings, character IDs or other private profile data.
/// Copy/paste rather than manual typing is the expected experience.
/// </summary>
internal static class GripShareCode
{
    internal sealed class Parsed
    {
        internal string ClassName = "";
        internal byte[] ControllerTag = Array.Empty<byte>();
        internal float[] Values = Array.Empty<float>();
    }

    private static readonly string[] Classes =
    {
        "Bard", "Runemage", "Warrior", "Paladin", "Musketeer",
        "Ranger", "Shaman", "Scoundrel"
    };
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const int RawBytes = 34; // version + class + controller hash[4] + floats[24] + checksum[4]
    private const int PayloadBytes = RawBytes - 4;
    private const string Prefix = "OW1-";
    internal static string[] SupportedClasses => (string[])Classes.Clone();

    internal static bool IsSupportedClass(string name) =>
        Classes.Any(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));

    internal static string CanonicalClass(string name) =>
        Classes.FirstOrDefault(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase))
        ?? "";

    private static byte[] ControllerTag(string controller)
    {
        byte[] digest = SHA256.HashData(
            Encoding.UTF8.GetBytes(controller.Trim().ToUpperInvariant()));
        return digest.Take(4).ToArray();
    }

    internal static bool IsCompatible(Parsed code, string controller) =>
        code.ControllerTag.SequenceEqual(ControllerTag(controller));

    // Indices 0-2 are rotation (degrees); 3-5 are local position (metres).
    // Limits protect against deliberately extreme shared offsets.
    internal static bool SafeValues(float[]? values) =>
        values != null && values.Length == 6 &&
        values.All(float.IsFinite) &&
        values.Take(3).All(v => Math.Abs(v) <= 720.0f) &&
        values.Skip(3).All(v => Math.Abs(v) <= 2.0f);

    internal static bool TryEncode(GripPreset preset, out string code, out string reason)
    {
        code = "";
        reason = "";
        string cls = CanonicalClass(preset.ClassName);
        if (cls.Length == 0)
        {
            reason = "This class isn't supported by the share-code format yet.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(preset.Controller) || preset.Controller.Length > 60 ||
            !SafeValues(preset.Values))
        {
            reason = "This preset contains values outside the safe sharing range.";
            return false;
        }
        var raw = new byte[RawBytes];
        raw[0] = 1;
        raw[1] = (byte)Array.IndexOf(Classes, cls);
        Buffer.BlockCopy(ControllerTag(preset.Controller), 0, raw, 2, 4);
        for (int i = 0; i < 6; i++)
            Buffer.BlockCopy(BitConverter.GetBytes(preset.Values[i]), 0, raw, 6 + i * 4, 4);
        byte[] checksum = SHA256.HashData(raw.Take(PayloadBytes).ToArray());
        Buffer.BlockCopy(checksum, 0, raw, PayloadBytes, 4);
        string encoded = EncodeBase32(raw);
        var groups = Enumerable.Range(0, (encoded.Length + 4) / 5)
            .Select(i => encoded.Substring(i * 5, Math.Min(5, encoded.Length - i * 5)));
        code = Prefix + string.Join("-", groups);
        return true;
    }

    internal static bool TryDecode(string? text, out Parsed code, out string reason)
    {
        code = new Parsed();
        reason = "";
        if (string.IsNullOrWhiteSpace(text) || text.Length > 160)
        {
            reason = "Paste a valid WeaponAdjustments share code.";
            return false;
        }
        string input = text.Trim().Trim((char)96).Trim().ToUpperInvariant();
        if (!input.StartsWith(Prefix, StringComparison.Ordinal))
        {
            reason = "That's not a WeaponAdjustments share code (expected OW1-).";
            return false;
        }
        string body = input.Substring(Prefix.Length).Replace("-", "").Replace(" ", "");
        if (body.Length != 55 || body.Any(c => Alphabet.IndexOf(c) < 0))
        {
            reason = "That code is incomplete or contains invalid characters.";
            return false;
        }
        if (!DecodeBase32(body, out byte[] raw) || raw.Length != RawBytes ||
            !string.Equals(EncodeBase32(raw), body, StringComparison.Ordinal))
        {
            reason = "The share code couldn't be decoded. Copy it again.";
            return false;
        }
        if (raw[0] != 1 || raw[1] >= Classes.Length)
        {
            reason = "This preset uses an unsupported code version or class.";
            return false;
        }
        byte[] expected = SHA256.HashData(raw.Take(PayloadBytes).ToArray());
        if (!raw.Skip(PayloadBytes).SequenceEqual(expected.Take(4)))
        {
            reason = "The code failed its integrity check. Copy it again.";
            return false;
        }
        var values = new float[6];
        for (int i = 0; i < 6; i++)
            values[i] = BitConverter.ToSingle(raw, 6 + i * 4);
        if (!SafeValues(values))
        {
            reason = "This code contains invalid or unsafe grip values.";
            return false;
        }
        code.ClassName = Classes[raw[1]];
        code.ControllerTag = raw.Skip(2).Take(4).ToArray();
        code.Values = values;
        return true;
    }

    private static string EncodeBase32(byte[] input)
    {
        var result = new StringBuilder((input.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (byte value in input)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                result.Append(Alphabet[(buffer >> bits) & 31]);
                buffer &= (1 << bits) - 1;
            }
        }
        if (bits > 0)
            result.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return result.ToString();
    }

    private static bool DecodeBase32(string text, out byte[] bytes)
    {
        var data = new byte[RawBytes];
        int buffer = 0, bits = 0, count = 0;
        foreach (char c in text)
        {
            int n = Alphabet.IndexOf(c);
            if (n < 0) { bytes = Array.Empty<byte>(); return false; }
            buffer = (buffer << 5) | n;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                if (count >= data.Length) { bytes = Array.Empty<byte>(); return false; }
                data[count++] = (byte)((buffer >> bits) & 255);
                buffer &= (1 << bits) - 1;
            }
        }
        if (count != RawBytes || buffer != 0)
        {
            bytes = Array.Empty<byte>();
            return false;
        }
        bytes = data;
        return true;
    }
}
