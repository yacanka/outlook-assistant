using System;
using System.Globalization;
using System.Linq;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Askai
{
    public enum CertificateTrustMode { ServerCertificate, CertificateAuthority }

    /// <summary>
    /// Handler-scoped public trust. CA mode requires a full path to the uploaded root.
    /// Server mode additionally pins its leaf; legacy single-leaf data keeps its original behavior.
    /// No system trust stores or global TLS policies are modified.
    /// </summary>
    internal sealed class ServerCertificateTrust
    {
        private readonly CertificateMaterial material;
        private readonly byte[] pinnedServer;
        private readonly byte[] trustedRoot;
        private readonly bool legacySingleLeaf;
        public CertificateTrustMode Mode { get; }
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
                foreach (var certificate in certificates) RequireCurrent(certificate);
                if (legacySingleLeaf)
                {
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
                // Selection checks topology/signatures without revocation; live TLS checks revocation online.
                foreach (var certificate in certificates) RequirePathToRoot(certificate, opened.Certificates);
                Description = "Mod: " + (mode == CertificateTrustMode.CertificateAuthority ? "CA zincirine güven" : "Sunucu + ara/kök zinciri")
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
            validation.ChainPolicy.RevocationMode = X509RevocationMode.Online;
            validation.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
            validation.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
            validation.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
            validation.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(5);
            return validation;
        }

        public bool Validate(X509Certificate2 certificate, X509Chain peerChain, SslPolicyErrors errors)
        {
            if (certificate == null || (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != SslPolicyErrors.None)
                return false; // Native hostname mismatch and missing/unknown peer errors must never be overridden.
            try
            {
                if (legacySingleLeaf)
                    return ValidateBuiltChain(certificate, peerChain)
                        && (errors == SslPolicyErrors.None || peerChain.ChainStatus.Any(status => status.Status != X509ChainStatusFlags.NoError));
                using (var opened = material.Open())
                using (var validation = CreateValidationChain())
                {
                    validation.ChainPolicy.ExtraStore.AddRange(opened.Certificates);
                    if (peerChain != null)
                        foreach (var element in peerChain.ChainElements) validation.ChainPolicy.ExtraStore.Add(element.Certificate);
                    validation.Build(certificate);
                    return ValidateBuiltChain(certificate, validation);
                }
            }
            catch (CryptographicException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (AiServiceException) { return false; }
        }

        // Also used with offline topology fixtures. The live callback always supplies a rebuilt Online chain.
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
            return new AiServiceException("Geçerli, tek bir kökte sonlanan zincir seçin. CA modunda yalnızca CA sertifikaları; sunucu modunda tek bir sunucu ve ara/kök sertifikaları gerekir. Eksik veya ilgisiz zincirler kabul edilmez.");
        }
    }
}
