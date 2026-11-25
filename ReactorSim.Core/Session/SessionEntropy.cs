// ReactorSim.Core - injected entropy source and identifier helpers for the session host (Phase 3, U3, KTD2, KTD6).
// Core holds only the interface: the transport supplies a cryptographic generator, tests supply a fixed one.

using System.Globalization;
using System.Text;

namespace ReactorSim.Session
{
    public interface ISessionEntropy
    {
        // Fills the whole buffer with entropy bytes.
        void Fill(byte[] buffer);
    }

    internal static class SessionIds
    {
        private const string HexDigits = "0123456789abcdef";

        public static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
            {
                sb.Append(HexDigits[bytes[i] >> 4]);
                sb.Append(HexDigits[bytes[i] & 0x0F]);
            }
            return sb.ToString();
        }

        // Lowercase hex only, exactly byteCount bytes.
        public static bool TryParseHex(string text, int byteCount, out byte[] bytes)
        {
            bytes = null;
            if (text == null || text.Length != byteCount * 2)
                return false;
            var result = new byte[byteCount];
            for (int i = 0; i < byteCount; i++)
            {
                int hi = HexDigits.IndexOf(text[i * 2]);
                int lo = HexDigits.IndexOf(text[i * 2 + 1]);
                if (hi < 0 || lo < 0)
                    return false;
                result[i] = (byte)((hi << 4) | lo);
            }
            bytes = result;
            return true;
        }

        // Six decimal digits from four entropy bytes. The modulo bias is negligible: the code guards against a
        // wrong session, not against an attacker (R10).
        public static string JoinCodeFrom(byte[] four)
        {
            uint v = ((uint)four[0] << 24) | ((uint)four[1] << 16) | ((uint)four[2] << 8) | four[3];
            return (v % 1000000u).ToString("D" + SessionProtocol.JoinCodeDigits, CultureInfo.InvariantCulture);
        }

        // netstandard2.1 has no fixed-time compare: touch every byte, and fold the length difference into the result.
        public static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null)
                return false;
            int diff = a.Length ^ b.Length;
            int n = a.Length < b.Length ? a.Length : b.Length;
            for (int i = 0; i < n; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
