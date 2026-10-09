using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Askai
{
    /// <summary>Bounded, public-only DER/PEM material. No keys or certificate stores are imported.</summary>
    internal sealed class CertificateMaterial
    {
        private const int MaxCertificateBytes = 65536;
        private const int MaxBundleBytes = 262144;
        private const int MaxSerializedBytes = 393216;
        private const int MaxCertificates = 16;
        private const string Begin = "-----BEGIN CERTIFICATE-----";
        private const string End = "-----END CERTIFICATE-----";
        private readonly byte[][] certificates;
        public int Count => certificates.Length;
        public string Encoded { get; }
        public string Fingerprint { get; }

        private CertificateMaterial(List<byte[]> data)
        {
            certificates = data.GroupBy(Hash, StringComparer.Ordinal).Select(group => group.First())
                .OrderBy(Hash, StringComparer.Ordinal).ToArray();
            byte[] canonical;
            if (certificates.Length == 1) canonical = certificates[0]; // Retain the old DER settings format.
            else
            {
                var pem = new StringBuilder();
                foreach (byte[] certificate in certificates)
                    pem.Append(Begin).Append('\n').Append(Convert.ToBase64String(certificate)).Append('\n').Append(End).Append('\n');
                canonical = Encoding.ASCII.GetBytes(pem.ToString());
            }
            Encoded = Convert.ToBase64String(canonical);
            Fingerprint = Hash(canonical);
        }

        public static CertificateMaterial FromFiles(string[] paths)
        {
            if (paths == null || paths.Length == 0 || paths.Length > MaxCertificates) throw InvalidMaterial();
            var data = new List<byte[]>();
            int total = 0;
            try
            {
                foreach (string path in paths)
                {
                    string extension = Path.GetExtension(path);
                    if (!string.Equals(extension, ".cer", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(extension, ".crt", StringComparison.OrdinalIgnoreCase)) throw InvalidMaterial();
                    byte[] bytes = ReadFile(path, MaxBundleBytes - total);
                    total += bytes.Length;
                    Parse(bytes, data);
                }
                return new CertificateMaterial(data);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                throw new AiServiceException("Sertifika dosyaları okunamadı. Erişilebilir .cer/.crt dosyaları seçin.", ex);
            }
        }

        public static CertificateMaterial FromBase64(string encoded)
        {
            try
            {
                if (string.IsNullOrEmpty(encoded) || encoded.Length > (MaxSerializedBytes + 2) / 3 * 4 + 4096)
                    throw InvalidMaterial();
                byte[] bytes = Convert.FromBase64String(encoded);
                if (bytes.Length > MaxSerializedBytes) throw InvalidMaterial();
                var data = new List<byte[]>();
                Parse(bytes, data);
                if (data.Sum(item => item.Length) > MaxBundleBytes) throw InvalidMaterial();
                return new CertificateMaterial(data);
            }
            catch (FormatException ex) { throw new AiServiceException("Kaydedilmiş sertifikalar okunamadı. Yeniden seçin veya temizleyin.", ex); }
        }

        private static byte[] ReadFile(string path, int limit)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var bytes = new byte[limit + 1];
                int length = 0;
                int count;
                while (length < bytes.Length && (count = stream.Read(bytes, length, bytes.Length - length)) > 0) length += count;
                if (length > limit) throw InvalidMaterial();
                Array.Resize(ref bytes, length);
                return bytes;
            }
        }

        private static void Parse(byte[] bytes, List<byte[]> data)
        {
            int offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            string text = Encoding.ASCII.GetString(bytes, offset, bytes.Length - offset).Trim();
            if (!text.StartsWith(Begin, StringComparison.Ordinal)) { AddDer(bytes, data); return; }
            int position = 0;
            try
            {
                while (position < text.Length)
                {
                    while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
                    if (position == text.Length) break;
                    if (text.IndexOf(Begin, position, StringComparison.Ordinal) != position) throw InvalidMaterial();
                    int start = position + Begin.Length;
                    int end = text.IndexOf(End, start, StringComparison.Ordinal);
                    if (end < 0) throw InvalidMaterial();
                    AddDer(Convert.FromBase64String(text.Substring(start, end - start)), data);
                    position = end + End.Length;
                }
            }
            catch (FormatException ex) { throw new AiServiceException("PEM dosyası yalnızca geçerli CERTIFICATE blokları içermelidir.", ex); }
        }

        private static void AddDer(byte[] bytes, List<byte[]> data)
        {
            if (data.Count >= MaxCertificates || bytes.Length == 0 || bytes.Length > MaxCertificateBytes) throw InvalidMaterial();
            using (var certificate = LoadPublic(bytes)) data.Add(certificate.RawData);
        }

        public CertificateSet Open() { return new CertificateSet(certificates); }

        private static X509Certificate2 LoadPublic(byte[] bytes)
        {
            try
            {
                if (X509Certificate2.GetCertContentType(bytes) != X509ContentType.Cert) throw InvalidMaterial();
                // Public DER constructor required by .NET Framework 4.7.2; PFX is rejected before loading.
#pragma warning disable SYSLIB0057
                var certificate = new X509Certificate2(bytes);
#pragma warning restore SYSLIB0057
                try
                {
                    if (certificate.HasPrivateKey || certificate.RawData.Length != bytes.Length) throw InvalidMaterial();
                    return certificate;
                }
                catch
                {
                    certificate.Dispose();
                    throw;
                }
            }
            catch (CryptographicException ex) { throw new AiServiceException("Sertifika verisi geçersiz. DER veya PEM .cer/.crt dosyaları seçin.", ex); }
        }

        private static string Hash(byte[] bytes)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "");
        }

        private static AiServiceException InvalidMaterial()
        {
            return new AiServiceException("Yalnızca .cer/.crt dosyalarında en fazla 16 açık sertifika seçin. Toplam dosya boyutu 256 KiB, tek sertifika 64 KiB olabilir. Özel anahtar ve PFX/PKCS#7 paketleri desteklenmez.");
        }

        internal sealed class CertificateSet : IDisposable
        {
            public X509Certificate2Collection Certificates { get; } = new X509Certificate2Collection();
            public CertificateSet(byte[][] data)
            {
                try { foreach (byte[] bytes in data) Certificates.Add(LoadPublic(bytes)); }
                catch { Dispose(); throw; }
            }
            public void Dispose() { foreach (var certificate in Certificates) certificate.Dispose(); }
        }
    }
}
