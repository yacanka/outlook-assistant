using System;
using System.IO;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Askai;

internal static class CertificateChainTests
{
    public static void Run()
    {
        using (var rootKey = RSA.Create(2048))
        using (var issuerKey = RSA.Create(2048))
        using (var leafKey = RSA.Create(2048))
        using (var foreignKey = RSA.Create(2048))
        using (var root = Create("CN=Test Root", rootKey, null, rootKey, true))
        using (var issuer = Create("CN=Test Issuer", issuerKey, root, rootKey, true))
        using (var leaf = Create("CN=mail.example.invalid", leafKey, issuer, issuerKey, false))
        using (var renewed = Create("CN=mail.example.invalid", leafKey, issuer, issuerKey, false))
        using (var foreign = Create("CN=Other Root", foreignKey, null, foreignKey, true))
        using (var wrongSignature = Create("CN=Invalid Issuer", issuerKey, root, foreignKey, true))
        using (var expiredLeaf = Create("CN=mail.example.invalid", leafKey, issuer, issuerKey, false, expired: true))
        using (var invalidLeaf = Create("CN=mail.example.invalid", leafKey, issuer, foreignKey, false))
        using (var full = Build(leaf, issuer, root))
        using (var renewedChain = Build(renewed, issuer, root))
        using (var incomplete = Build(leaf, root))
        {
            string directory = Path.Combine(Path.GetTempPath(), "askai-chain-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string rootFile = Write(directory, "root.cer", root);
                string issuerFile = Write(directory, "issuer.crt", issuer);
                string leafFile = Write(directory, "server.cer", leaf);
                string foreignFile = Write(directory, "other.cer", foreign);
                string badFile = Write(directory, "bad.crt", wrongSignature);
                var ca = ServerCertificateTrust.FromFiles(new[] { issuerFile, rootFile }, CertificateTrustMode.CertificateAuthority);
                var pinned = ServerCertificateTrust.FromFiles(new[] { rootFile, leafFile, issuerFile }, CertificateTrustMode.ServerCertificate);
                Check(ca.CertificateCount == 2 && pinned.CertificateCount == 3, "each mode imports its complete public material");
                // A requests-style bundle authenticates the chain without requiring CRL availability
                // or pinning the bundled leaf; exercise the live callback, not only the offline helper.
                var bundleMode = CertificateTrustMode.CertificateBundle;
                var requestsBundle = ServerCertificateTrust.FromFiles(new[] { leafFile, issuerFile, rootFile }, bundleMode);
                Check(requestsBundle.Validate(leaf, incomplete, SslPolicyErrors.RemoteCertificateChainErrors),
                    "bundle callback builds missing intermediates from the uploaded chain without CRL endpoints");
                string expiredFile = Write(directory, "expired-server.cer", expiredLeaf);
                var staleLeafBundle = ServerCertificateTrust.FromFiles(new[] { expiredFile, issuerFile, rootFile }, bundleMode);
                Check(staleLeafBundle.Validate(renewed, renewedChain, SslPolicyErrors.RemoteCertificateChainErrors),
                    "an expired optional bundled leaf cannot block a valid renewed server");
                Check(!staleLeafBundle.Validate(expiredLeaf, full, SslPolicyErrors.None),
                    "actual expired server remains rejected in bundle mode");
                Check(requestsBundle.Validate(renewed, renewedChain, SslPolicyErrors.RemoteCertificateChainErrors),
                    "bundle trusts the issuing CA rather than pinning the old leaf");
                Check(!requestsBundle.Validate(leaf, full, SslPolicyErrors.RemoteCertificateNameMismatch),
                    "bundle mode still rejects hostname mismatch");
                Check(!requestsBundle.Validate(invalidLeaf, full, SslPolicyErrors.None),
                    "bundle rebuild rejects an invalid server signature even without native error flags");
                Check(!requestsBundle.Validate(null, full, SslPolicyErrors.RemoteCertificateNotAvailable),
                    "bundle requires a peer certificate");
                var foreignBundle = ServerCertificateTrust.FromFiles(new[] { foreignFile }, bundleMode);
                Check(!foreignBundle.Validate(leaf, full, SslPolicyErrors.RemoteCertificateChainErrors),
                    "bundle rejects a server issued by an unrelated CA");
                Throws<AiServiceException>(() => ServerCertificateTrust.FromFiles(new[] { leafFile, issuerFile }, bundleMode));
                Throws<AiServiceException>(() => ServerCertificateTrust.FromFiles(new[] { leafFile }, bundleMode));
                Throws<AiServiceException>(() => ServerCertificateTrust.FromFiles(new[] { rootFile, badFile }, bundleMode));
                Check(ServerCertificateTrust.FromBase64(requestsBundle.EncodedCertificate, bundleMode)
                    .Validate(leaf, full, SslPolicyErrors.RemoteCertificateChainErrors), "bundle roundtrip retains validation policy");
                using (var handler = AiHttpTransport.CreateHandler(false, requestsBundle))
                {
                    Check(!handler.CheckCertificateRevocationList, "native handler does not impose an extra CRL requirement in bundle mode");
                    Check(handler.ServerCertificateCustomValidationCallback(null, leaf, incomplete, SslPolicyErrors.RemoteCertificateChainErrors),
                        "actual bundle handler enforces the rebuilt certificate chain");
                }
                Check(!ReferenceEquals(
                    AiHttpTransport.GetClient(false, serverCertificateBase64: pinned.EncodedCertificate, certificateTrustMode: CertificateTrustMode.ServerCertificate),
                    AiHttpTransport.GetClient(false, serverCertificateBase64: pinned.EncodedCertificate, certificateTrustMode: bundleMode)),
                    "revocation and pinning policies never share an established connection");

                Check(ca.ValidateBuiltChain(leaf, full), "uploaded CA anchor accepts a valid issued server certificate");
                Check(ca.ValidateBuiltChain(renewed, renewedChain), "CA mode permits certificate renewal under the same root");
                Check(pinned.ValidateBuiltChain(leaf, full), "server chain mode accepts its exact leaf and complete root path");
                Check(!pinned.ValidateBuiltChain(renewed, renewedChain), "server chain mode rejects a replacement leaf");
                Check(!ca.ValidateBuiltChain(leaf, incomplete), "partial chains cannot establish CA trust");
                try
                {
                    ServerCertificateTrust.FromFiles(new[] { rootFile }, CertificateTrustMode.ServerCertificate);
                    throw new Exception("A CA must not be silently pinned as the peer server certificate");
                }
                catch (AiServiceException error)
                {
                    Check(error.Message.Contains("CA"), "Wrong certificate mode explains how to correct selection");
                }
                var otherCa = ServerCertificateTrust.FromFiles(new[] { foreignFile }, CertificateTrustMode.CertificateAuthority);
                Check(!otherCa.ValidateBuiltChain(leaf, full), "a different root is rejected even if the native path is valid");
                Check(!ca.Validate(leaf, full, SslPolicyErrors.RemoteCertificateNameMismatch), "CA mode never overrides hostname mismatch");
                Check(!pinned.Validate(leaf, full, SslPolicyErrors.RemoteCertificateNotAvailable), "server chain mode fails closed for missing peer");
                using (var validation = ca.CreateValidationChain())
                {
                    Check(validation.ChainPolicy.RevocationMode == X509RevocationMode.Online
                        && validation.ChainPolicy.VerificationFlags == X509VerificationFlags.NoFlag,
                        "TLS chain rebuild requires revocation and signature/usage checks");
                }
                Throws<AiServiceException>(() => ServerCertificateTrust.FromFiles(new[] { rootFile, issuerFile, leafFile }, CertificateTrustMode.CertificateAuthority));
                Throws<AiServiceException>(() => ServerCertificateTrust.FromFiles(new[] { issuerFile }, CertificateTrustMode.CertificateAuthority));
                Throws<AiServiceException>(() => ServerCertificateTrust.FromFiles(new[] { rootFile, leafFile }, CertificateTrustMode.ServerCertificate));
                Throws<AiServiceException>(() => ServerCertificateTrust.FromFiles(new[] { rootFile, badFile }, CertificateTrustMode.CertificateAuthority));
                Throws<AiServiceException>(() => ServerCertificateTrust.FromFiles(new[] { rootFile, issuerFile, foreignFile }, CertificateTrustMode.CertificateAuthority));

                var reversed = ServerCertificateTrust.FromFiles(new[] { rootFile, issuerFile }, CertificateTrustMode.CertificateAuthority);
                Check(reversed.Fingerprint == ca.Fingerprint, "file order cannot change the policy identity");
                Check(ServerCertificateTrust.FromBase64(ca.EncodedCertificate, CertificateTrustMode.CertificateAuthority).Fingerprint == ca.Fingerprint,
                    "CA bundle roundtrips through settings");
                Check(ServerCertificateTrust.FromBase64(pinned.EncodedCertificate).Fingerprint == pinned.Fingerprint,
                    "server bundle roundtrips through settings");
                Check(ServerCertificateTrust.FromBase64(Convert.ToBase64String(leaf.RawData)).CertificateCount == 1,
                    "legacy single DER settings remain readable");
                string bundle = Path.Combine(directory, "chain.crt");
                File.WriteAllText(bundle, Pem(root) + Pem(leaf) + Pem(issuer));
                Check(ServerCertificateTrust.FromFiles(new[] { bundle }, CertificateTrustMode.ServerCertificate).Fingerprint == pinned.Fingerprint,
                    "multi-block PEM imports the same unordered chain");
                Check(ServerCertificateTrust.FromFiles(new[] { bundle }, bundleMode)
                    .Validate(leaf, incomplete, SslPolicyErrors.RemoteCertificateChainErrors),
                    "a single PEM file with root, intermediate and server works in bundle mode");
                string forbidden = Path.Combine(directory, "chain.pem");
                File.WriteAllText(forbidden, Pem(root));
                Throws<AiServiceException>(() => ServerCertificateTrust.FromFiles(new[] { forbidden }, CertificateTrustMode.CertificateAuthority));

                string rootData = Convert.ToBase64String(root.RawData);
                Check(!ReferenceEquals(
                    AiHttpTransport.GetClient(false, serverCertificateBase64: Convert.ToBase64String(leaf.RawData), certificateTrustMode: CertificateTrustMode.ServerCertificate),
                    AiHttpTransport.GetClient(false, serverCertificateBase64: rootData, certificateTrustMode: CertificateTrustMode.CertificateAuthority)),
                    "trust modes do not share connection pools even for identical DER input");
                Check(ServerCertificateTrust.ParseMode(null) == CertificateTrustMode.ServerCertificate, "old settings default to server mode");
                Check(ServerCertificateTrust.ParseMode("CertificateBundle") == bundleMode, "saved bundle mode is recognized");
                Throws<FormatException>(() => ServerCertificateTrust.ParseMode("Unknown"));
            }
            finally { Directory.Delete(directory, true); } // Only this test's temporary public-certificate directory.
        }
        Console.WriteLine("Certificate chain checks passed (CA/server modes, renewal, roots, signatures, PEM/files, settings compatibility).");
    }

    private static X509Certificate2 Create(string name, RSA key, X509Certificate2 issuer, RSA signer, bool ca, bool expired = false)
    {
        var request = new CertificateRequest(name, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(ca, ca, ca ? 3 : 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(ca
            ? X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.DigitalSignature
            : X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, true));
        return request.Create(issuer == null ? request.SubjectName : issuer.SubjectName,
            X509SignatureGenerator.CreateForRSA(signer, RSASignaturePadding.Pkcs1),
            DateTimeOffset.UtcNow.AddDays(expired ? -10 : -1), DateTimeOffset.UtcNow.AddDays(expired ? -1 : 5), Guid.NewGuid().ToByteArray());
    }

    private static X509Chain Build(X509Certificate2 leaf, params X509Certificate2[] issuers)
    {
        var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck; // Offline topology fixture; live builder is asserted Online separately.
        chain.ChainPolicy.ExtraStore.AddRange(issuers);
        chain.Build(leaf);
        return chain;
    }
    private static string Write(string directory, string name, X509Certificate2 certificate)
    {
        string path = Path.Combine(directory, name);
        File.WriteAllBytes(path, certificate.RawData);
        return path;
    }
    private static string Pem(X509Certificate2 certificate)
    {
        return "-----BEGIN CERTIFICATE-----\n" + Convert.ToBase64String(certificate.RawData) + "\n-----END CERTIFICATE-----\n";
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); }
}
