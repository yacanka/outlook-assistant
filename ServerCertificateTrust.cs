using System;
using System.Globalization;
using System.Linq;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Askai
{
    public enum CertificateTrustMode { ServerCertificate, CertificateAuthority, CertificateBundle }

    /// <summary>
    /// Handler-scoped public trust. CA mode requires a full path to the uploaded root.
    /// Server mode additionally pins its leaf; legacy single-leaf data keeps its original behavior.
    /// Bundle mode trusts its root without leaf pinning or online revocation, only when explicitly selected.
    /// No system trust stores or global TLS policies are modified.
    /// </summary>
    internal sealed class ServerCertificateTrust
    {
        private readonly CertificateMaterial material;
        private readonly byte[] pinnedServer;
        private readonly byte[] trustedRoot;
        private readonly bool legacySingleLeaf;
        public CertificateTrustMode Mode { get; }
        public bool CheckRevocation => Mode != CertificateTrustMode.CertificateBundle;
        public int CertificateCount => material.Count;
        public string Fingerprint => material.Fingerprint;
        public string Description { get; }
        public string EncodedCertificate => material.Encoded;

        private ServerCertificateTrust(CertificateMaterial material, CertificateTrustMode mode)
        {
            if (!Enum.IsDefined(typeof(CertificateTrustMode), mode)) throw InvalidChain();
            this.material = material;
            Mode = mode;
            legacySingleLeaf = mode == CertificateTrustMode.ServerCertificate && material.Count == 1;
            using (var opened = material.Open())
            {
                var certificates = opened.Certificates.Cast<X509Certificate2>().ToArray();
                // In bundle mode, optional leaves are not trust anchors. Their expiry must not
                // block a renewed live server; the callback validates the actual peer instead.
                var requiredCertificates = mode == CertificateTrustMode.CertificateBundle
                    ? certificates.Where(IsAuthority).ToArray() : certificates;
                foreach (var certificate in requiredCertificates) RequireCurrent(certificate);
                if (legacySingleLeaf)
                {
                    if (IsAuthority(certificates[0]))
                        throw new AiServiceException("Seçilen sertifika bir CA sertifikasıdır. AI Ayarları'nda CA zincirine güven modunu seçip kök/ara CA dosyalarını yeniden yükleyin.");
                    if (!HasServerUsage(certificates[0])) throw InvalidChain();
                    pinnedServer = certificates[0].RawData;
                    Description = "Sunucu: " + certificates[0].GetNameInfo(X509NameType.DnsName, false)
                        + "\r\nSon geçerlilik: " + certificates[0].NotAfter.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)
                        + "\r\nSHA-256: " + Fingerprint;
                    return;
                }

                var authorities = certificates.Where(IsAuthority).ToArray();
                var roots = authorities.Where(IsRoot).ToArray();
                if (roots.Length != 1) throw InvalidChain();
                trustedRoot = roots[0].RawData;
                foreach (var authority in authorities)
                    if (!HasAuthorityUsage(authority)) throw InvalidChain();
                var servers = certificates.Where(cert => !IsAuthority(cert)).ToArray();
                if (mode == CertificateTrustMode.CertificateAuthority && servers.Length != 0) throw InvalidChain();
                if (mode == CertificateTrustMode.ServerCertificate)
                {
                    if (servers.Length != 1 || !HasServerUsage(servers[0])) throw InvalidChain();
                    pinnedServer = servers[0].RawData;
                }
                // Import checks topology/signatures. Live revocation is controlled by the explicit trust mode.
                foreach (var certificate in requiredCertificates) RequirePathToRoot(certificate, opened.Certificates);
                Description = "Mod: " + (mode == CertificateTrustMode.CertificateBundle ? "Sertifika paketi (CRL/OCSP kapalı)"
                    : mode == CertificateTrustMode.CertificateAuthority ? "CA zincirine güven" : "Sunucu + ara/kök zinciri")
                    + "\r\nSertifika sayısı: " + material.Count
                    + "\r\nKök CA: " + roots[0].GetNameInfo(X509NameType.SimpleName, false)
                    + (servers.Length == 1 ? "\r\nSunucu: " + servers[0].GetNameInfo(X509NameType.DnsName, false) : "")
                    + "\r\nZincir SHA-256: " + Fingerprint;
            }
        }

        public static CertificateTrustMode ParseMode(string text)
        {
            if (text == null) return CertificateTrustMode.ServerCertificate;
            CertificateTrustMode mode;
            if (!Enum.TryParse(text, out mode) || !Enum.IsDefined(typeof(CertificateTrustMode), mode)) throw new FormatException();
            return mode;
        }

        public static ServerCertificateTrust FromFile(string path)
        {
            return FromFiles(new[] { path }, CertificateTrustMode.ServerCertificate);
        }
        public static ServerCertificateTrust FromFiles(string[] paths, CertificateTrustMode mode)
        {
            return Create(CertificateMaterial.FromFiles(paths), mode);
        }
        public static ServerCertificateTrust FromBase64(string encoded, CertificateTrustMode mode = CertificateTrustMode.ServerCertificate)
        {
            return Create(CertificateMaterial.FromBase64(encoded), mode);
        }

        private static ServerCertificateTrust Create(CertificateMaterial material, CertificateTrustMode mode)
        {
            try { return new ServerCertificateTrust(material, mode); }
            catch (CryptographicException ex)
            {
                throw new AiServiceException("Sertifika zinciri doğrulanamadı. Geçerli .cer/.crt sertifikaları seçin.", ex);
            }
        }

        private void RequirePathToRoot(X509Certificate2 certificate, X509Certificate2Collection certificates)
        {
            using (var validation = CreateValidationChain())
            {
                validation.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck; // Import does not authenticate a live connection.
                validation.ChainPolicy.ExtraStore.AddRange(certificates);
                validation.Build(certificate);
                if (!IsAnchoredPath(certificate, validation, false)) throw InvalidChain();
                // ExtraStore is not exclusive: Windows may complete paths from its cache/store/AIA.
                // Import must remain portable and validate only the selected public material.
                foreach (var element in validation.ChainElements)
                    if (!certificates.Cast<X509Certificate2>().Any(selected => Equal(selected.RawData, element.Certificate.RawData)))
                        throw InvalidChain();
            }
        }

        public X509Chain CreateValidationChain()
        {
            var validation = new X509Chain();
            validation.ChainPolicy.RevocationMode = CheckRevocation ? X509RevocationMode.Online : X509RevocationMode.NoCheck;
            validation.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
            validation.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
            validation.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
            validation.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(5);
            return validation;
        }

        public bool Validate(X509Certificate2 certificate, X509Chain peerChain, SslPolicyErrors errors)
        {
            string failure;
            return Validate(certificate, peerChain, errors, out failure);
        }

        public bool Validate(X509Certificate2 certificate, X509Chain peerChain, SslPolicyErrors errors, out string failure)
        {
            failure = "Sunucu sertifikası doğrulanamadı.";
            if (certificate == null || (errors & SslPolicyErrors.RemoteCertificateNotAvailable) != 0)
            {
                failure = "Sunucu sertifikası alınamadı.";
                return false;
            }
            if ((errors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
            {
                failure = "Sunucu adresi sertifikadaki adla eşleşmiyor. Central HTTPS adresini ve proxy sertifikasını kontrol edin.";
                return false;
            }
            if ((errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != SslPolicyErrors.None) return false;
            try
            {
                if (legacySingleLeaf)
                {
                    bool valid = ValidateBuiltChain(certificate, peerChain)
                        && (errors == SslPolicyErrors.None || peerChain.ChainStatus.Any(status => status.Status != X509ChainStatusFlags.NoError));
                    failure = valid ? null : DescribeFailure(certificate, peerChain);
                    return valid;
                }
                using (var opened = material.Open())
                using (var validation = CreateValidationChain())
                {
                    validation.ChainPolicy.ExtraStore.AddRange(opened.Certificates);
                    if (peerChain != null)
                        foreach (var element in peerChain.ChainElements) validation.ChainPolicy.ExtraStore.Add(element.Certificate);
                    validation.Build(certificate);
                    bool valid = ValidateBuiltChain(certificate, validation);
                    failure = valid ? null : DescribeFailure(certificate, validation);
                    return valid;
                }
            }
            catch (CryptographicException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (AiServiceException) { return false; }
        }

        private string DescribeFailure(X509Certificate2 certificate, X509Chain chain)
        {
            if (!IsCurrent(certificate)) return "Sunucu sertifikasının süresi geçerli değil. Sertifikayı ve Windows saatini kontrol edin.";
            if (pinnedServer != null && !Equal(pinnedServer, certificate.RawData))
                return "Sunucunun sunduğu sertifika yüklenen sunucu sertifikasıyla eşleşmiyor. Sertifika yenilenmiş veya proxy tarafından değiştirilmiş olabilir; doğru sunucu sertifikasını yeniden seçin.";
            if (!HasServerUsage(certificate)) return "Sertifika sunucu kimlik doğrulaması için uygun değil.";
            if (chain == null || chain.ChainElements.Count == 0) return "Sunucu sertifika zinciri oluşturulamadı.";
            var flags = X509ChainStatusFlags.NoError;
            foreach (var status in chain.ChainStatus) flags |= status.Status;
            if ((flags & X509ChainStatusFlags.Revoked) != 0) return "Sunucu sertifika zincirinde iptal edilmiş sertifika var.";
            if ((flags & X509ChainStatusFlags.PartialChain) != 0 && !legacySingleLeaf)
                return "Sertifika zinciri eksik. Doğru kök ve ara CA sertifikalarını yükleyin.";
            if ((flags & (X509ChainStatusFlags.OfflineRevocation | X509ChainStatusFlags.RevocationStatusUnknown)) != 0)
                return "Sertifika iptal durumu doğrulanamadı (CRL/OCSP). Windows'un iptal servisine ağ/proxy erişimini ve sunucunun iptal bilgilerini kontrol edin.";
            if (trustedRoot != null && !Equal(trustedRoot, chain.ChainElements[chain.ChainElements.Count - 1].Certificate.RawData))
                return "Sunucu sertifika zinciri yüklenen kök CA'da sonlanmıyor. Doğru CA zincirini seçin.";
            // Only enum flags, never native status text, names, URLs or provider data.
            return "Sunucu sertifika zinciri reddedildi. Zincir durumu: " + flags + ".";
        }

        // Also used with topology fixtures. Bundle/CA/server-chain callbacks rebuild using their selected policy.
        public bool ValidateBuiltChain(X509Certificate2 certificate, X509Chain chain)
        {
            try
            {
                if (certificate == null || !IsCurrent(certificate) || !HasServerUsage(certificate)) return false;
                if (pinnedServer != null && !Equal(pinnedServer, certificate.RawData)) return false;
                return IsAnchoredPath(certificate, chain, legacySingleLeaf);
            }
            catch (CryptographicException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        private bool IsAnchoredPath(X509Certificate2 certificate, X509Chain chain, bool allowPartial)
        {
            if (chain == null || chain.ChainElements.Count == 0
                || !Equal(certificate.RawData, chain.ChainElements[0].Certificate.RawData)) return false;
            if (trustedRoot != null && !Equal(trustedRoot, chain.ChainElements[chain.ChainElements.Count - 1].Certificate.RawData))
                return false; // Never fall back to an unrelated OS-trusted root.
            foreach (var element in chain.ChainElements) if (!IsCurrent(element.Certificate)) return false;
            foreach (var status in chain.ChainStatus) if (!IsPermittedStatus(status.Status, allowPartial)) return false;
            return true;
        }

        public static bool IsPermittedStatus(X509ChainStatusFlags status, bool allowPartialChain = true)
        {
            var allowed = X509ChainStatusFlags.UntrustedRoot;
            if (allowPartialChain) allowed |= X509ChainStatusFlags.PartialChain;
            return (status & ~allowed) == X509ChainStatusFlags.NoError;
        }

        private static bool IsAuthority(X509Certificate2 certificate)
        {
            foreach (var extension in certificate.Extensions)
                if (extension.Oid.Value == "2.5.29.19")
                    return new X509BasicConstraintsExtension(extension, extension.Critical).CertificateAuthority;
            return false;
        }
        private static bool IsRoot(X509Certificate2 certificate)
        {
            return Equal(certificate.SubjectName.RawData, certificate.IssuerName.RawData);
        }
        private static bool HasAuthorityUsage(X509Certificate2 certificate)
        {
            if (!HasServerEku(certificate)) return false;
            foreach (var extension in certificate.Extensions)
                if (extension.Oid.Value == "2.5.29.15")
                    return (new X509KeyUsageExtension(extension, extension.Critical).KeyUsages & X509KeyUsageFlags.KeyCertSign) != 0;
            return true;
        }
        private static bool HasServerUsage(X509Certificate2 certificate)
        {
            if (!HasServerEku(certificate)) return false;
            foreach (var extension in certificate.Extensions)
                if (extension.Oid.Value == "2.5.29.15")
                {
                    var usage = new X509KeyUsageExtension(extension, extension.Critical).KeyUsages;
                    return (usage & (X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.KeyAgreement)) != 0;
                }
            return true;
        }
        private static bool HasServerEku(X509Certificate2 certificate)
        {
            foreach (var extension in certificate.Extensions)
                if (extension.Oid.Value == "2.5.29.37")
                    return new X509EnhancedKeyUsageExtension(extension, extension.Critical).EnhancedKeyUsages.Cast<Oid>()
                        .Any(oid => oid.Value == "1.3.6.1.5.5.7.3.1" || oid.Value == "2.5.29.37.0");
            return true;
        }
        private static bool IsCurrent(X509Certificate2 certificate)
        {
            DateTime now = DateTime.UtcNow;
            return now >= certificate.NotBefore.ToUniversalTime() && now <= certificate.NotAfter.ToUniversalTime();
        }
        private static void RequireCurrent(X509Certificate2 certificate)
        {
            if (!IsCurrent(certificate)) throw new AiServiceException("Sertifikalardan biri henüz geçerli değil veya süresi dolmuş. Geçerli zinciri seçin ve Windows saatini kontrol edin.");
        }
        private static bool Equal(byte[] left, byte[] right)
        {
            return left.Length == right.Length && left.SequenceEqual(right);
        }
        private static AiServiceException InvalidChain()
        {
            return new AiServiceException("Geçerli, tek bir kökte sonlanan zincir seçin. CA modunda yalnızca CA sertifikaları; sunucu modunda tek bir sunucu ve ara/kök sertifikaları gerekir. Paket modunda kök ve ara sertifikalara ek olarak sunucu sertifikası bulunabilir. Eksik veya ilgisiz zincirler kabul edilmez.");
        }
    }
}
