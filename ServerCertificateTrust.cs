using System;
using System.Globalization;
using System.IO;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Askai
{
    /// <summary>
    /// Explicit trust in one public server certificate, scoped to an AI HTTP handler.
    /// Exact DER identity, hostname, lifetime, server usage and non-trust chain errors
    /// remain enforced. This neither imports a CA nor changes Windows trust stores.
    /// </summary>
    internal sealed class ServerCertificateTrust
    {
        private const int MaxCertificateBytes = 65536;
        private const string PemStart = "-----BEGIN CERTIFICATE-----";
        private const string PemEnd = "-----END CERTIFICATE-----";
        private readonly byte[] expectedCertificate;
        private readonly DateTime validFromUtc;
        private readonly DateTime validUntilUtc;
        public string Fingerprint { get; }
        public string Description { get; }
        public string EncodedCertificate => Convert.ToBase64String(expectedCertificate);

        private ServerCertificateTrust(X509Certificate2 certificate)
        {
            expectedCertificate = certificate.RawData;
            validFromUtc = certificate.NotBefore.ToUniversalTime();
            validUntilUtc = certificate.NotAfter.ToUniversalTime();
            using (var hash = SHA256.Create())
                Fingerprint = BitConverter.ToString(hash.ComputeHash(expectedCertificate)).Replace("-", "");
            Description = "Sunucu: " + certificate.GetNameInfo(X509NameType.DnsName, false)
                + "\r\nSon geçerlilik: " + certificate.NotAfter.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)
                + "\r\nSHA-256: " + Fingerprint;
        }

        public static ServerCertificateTrust FromFile(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    // Read at most the limit plus one byte, even if a file changes size.
                    var bytes = new byte[MaxCertificateBytes + 1];
                    int length = 0;
                    int count;
                    while (length < bytes.Length && (count = stream.Read(bytes, length, bytes.Length - length)) > 0)
                        length += count;
                    if (length > MaxCertificateBytes) throw InvalidCertificate();
                    Array.Resize(ref bytes, length);
                    return FromBytes(DecodePem(bytes));
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                throw new AiServiceException("Sunucu sertifikası dosyası okunamadı. Erişilebilir bir .cer veya .crt dosyası seçin.", ex);
            }
        }

        public static ServerCertificateTrust FromBase64(string encoded)
        {
            try
            {
                if (encoded == null || encoded.Length > ((MaxCertificateBytes + 2) / 3 * 4) + 4096)
                    throw InvalidCertificate();
                return FromBytes(Convert.FromBase64String(encoded));
            }
            catch (FormatException ex) { throw new AiServiceException("Kaydedilmiş sunucu sertifikası okunamadı. Yeniden seçin veya temizleyin.", ex); }
        }

        private static byte[] DecodePem(byte[] bytes)
        {
            int offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            string text = Encoding.ASCII.GetString(bytes, offset, bytes.Length - offset).Trim();
            if (!text.StartsWith(PemStart, StringComparison.Ordinal)) return bytes;
            if (!text.EndsWith(PemEnd, StringComparison.Ordinal)) throw InvalidCertificate();
            try
            {
                // Exactly one certificate: multiple blocks or other PEM objects fail base64 decoding.
                return Convert.FromBase64String(text.Substring(PemStart.Length, text.Length - PemStart.Length - PemEnd.Length));
            }
            catch (FormatException ex) { throw new AiServiceException("Dosya tek bir geçerli sunucu sertifikası içermelidir.", ex); }
        }

        private static ServerCertificateTrust FromBytes(byte[] bytes)
        {
            try
            {
                if (bytes.Length == 0 || bytes.Length > MaxCertificateBytes
                    || X509Certificate2.GetCertContentType(bytes) != X509ContentType.Cert)
                    throw InvalidCertificate();
                // This public-certificate constructor is required by the .NET Framework 4.7.2 target.
#pragma warning disable SYSLIB0057
                using (var certificate = new X509Certificate2(bytes))
#pragma warning restore SYSLIB0057
                {
                    if (certificate.HasPrivateKey || certificate.RawData.Length != bytes.Length) throw InvalidCertificate();
                    DateTime now = DateTime.UtcNow;
                    if (now < certificate.NotBefore.ToUniversalTime() || now > certificate.NotAfter.ToUniversalTime())
                        throw new AiServiceException("Sunucu sertifikası henüz geçerli değil veya süresi dolmuş. Geçerli sertifikayı seçin ve Windows saatini kontrol edin.");
                    if (!HasServerUsage(certificate))
                        throw new AiServiceException("Seçilen sertifika TLS sunucu kullanımı için uygun değil. Sunucunun .cer/.crt sertifikasını seçin.");
                    return new ServerCertificateTrust(certificate);
                }
            }
            catch (CryptographicException ex) { throw new AiServiceException("Sunucu sertifikası geçersiz. Tek bir DER veya PEM .cer/.crt sertifikası seçin.", ex); }
        }

        private static bool HasServerUsage(X509Certificate2 certificate)
        {
            foreach (var extension in certificate.Extensions)
            {
                if (extension.Oid.Value == "2.5.29.37")
                {
                    var usages = new X509EnhancedKeyUsageExtension(extension, extension.Critical);
                    bool serverAllowed = false;
                    foreach (Oid usage in usages.EnhancedKeyUsages)
                        serverAllowed |= usage.Value == "1.3.6.1.5.5.7.3.1" || usage.Value == "2.5.29.37.0";
                    if (!serverAllowed) return false;
                }
                if (extension.Oid.Value == "2.5.29.15")
                {
                    var usage = new X509KeyUsageExtension(extension, extension.Critical);
                    var usable = X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.KeyAgreement;
                    if ((usage.KeyUsages & usable) == 0) return false;
                }
            }
            return true; // Missing usage extensions mean unconstrained usage, not client-only usage.
        }

        public bool Validate(X509Certificate2 certificate, X509Chain chain, SslPolicyErrors errors)
        {
            if ((errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != SslPolicyErrors.None)
                return false; // Never bypass hostname mismatch, missing certificate or unknown errors.
            DateTime now = DateTime.UtcNow;
            if (now < validFromUtc || now > validUntilUtc || certificate == null || chain == null) return false;
            try
            {
                if (!Matches(certificate) || chain.ChainElements.Count == 0 || !Matches(chain.ChainElements[0].Certificate))
                    return false;
                bool hasTrustError = false;
                foreach (var status in chain.ChainStatus)
                {
                    if (!IsPermittedStatus(status.Status)) return false;
                    hasTrustError |= status.Status != X509ChainStatusFlags.NoError;
                }
                // A chain failure with no known trust-only status must fail closed.
                return errors == SslPolicyErrors.None || hasTrustError;
            }
            catch (CryptographicException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        public static bool IsPermittedStatus(X509ChainStatusFlags status)
        {
            const X509ChainStatusFlags trustOnly = X509ChainStatusFlags.UntrustedRoot | X509ChainStatusFlags.PartialChain;
            return (status & ~trustOnly) == X509ChainStatusFlags.NoError;
        }

        private bool Matches(X509Certificate2 certificate)
        {
            byte[] actual = certificate.RawData;
            if (actual.Length != expectedCertificate.Length) return false;
            for (int index = 0; index < actual.Length; index++)
                if (actual[index] != expectedCertificate[index]) return false;
            return true;
        }

        private static AiServiceException InvalidCertificate()
        {
            return new AiServiceException("En fazla 64 KiB boyutunda, yalnızca açık anahtar içeren tek bir sunucu sertifikası (.cer/.crt) seçin. PFX, özel anahtar ve sertifika paketleri desteklenmez.");
        }
    }
}
