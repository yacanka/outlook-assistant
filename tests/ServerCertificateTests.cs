using System;
using System.IO;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Askai;

internal static class ServerCertificateTests
{
    public static void Run()
    {
        using (var key = RSA.Create(2048))
        using (var original = CreateCertificate(key, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(5), true))
        using (var rotated = CreateCertificate(key, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(6), true))
        using (var chain = BuildChain(original))
        using (var otherChain = BuildChain(rotated))
        {
            string encoded = Convert.ToBase64String(original.Export(X509ContentType.Cert));
            var trust = ServerCertificateTrust.FromBase64(encoded);
            Check(trust.Validate(original, chain, SslPolicyErrors.RemoteCertificateChainErrors),
                "selected self-signed server certificate is trusted locally");
            Check(trust.Validate(original, chain, SslPolicyErrors.None), "selected certificate with OS trust remains accepted");
            Check(!trust.Validate(rotated, otherChain, SslPolicyErrors.None), "different certificate rejected even with OS trust");
            Check(!trust.Validate(original, chain, SslPolicyErrors.RemoteCertificateNameMismatch), "hostname mismatch cannot be bypassed");
            Check(!trust.Validate(original, chain, SslPolicyErrors.RemoteCertificateNotAvailable), "missing certificate rejected");
            Check(!trust.Validate(null, chain, SslPolicyErrors.RemoteCertificateChainErrors), "null certificate rejected");
            Check(!trust.Validate(original, null, SslPolicyErrors.RemoteCertificateChainErrors), "missing chain rejected");
            Check(!trust.Validate(original, otherChain, SslPolicyErrors.RemoteCertificateChainErrors), "wrong chain rejected");

            Check(ServerCertificateTrust.IsPermittedStatus(X509ChainStatusFlags.UntrustedRoot | X509ChainStatusFlags.PartialChain),
                "only explicit leaf trust replaces missing issuer/root trust");
            foreach (var forbidden in new[] { X509ChainStatusFlags.NotTimeValid, X509ChainStatusFlags.Revoked,
                X509ChainStatusFlags.NotSignatureValid, X509ChainStatusFlags.NotValidForUsage,
                X509ChainStatusFlags.InvalidBasicConstraints, X509ChainStatusFlags.ExplicitDistrust,
                X509ChainStatusFlags.OfflineRevocation, X509ChainStatusFlags.RevocationStatusUnknown })
                Check(!ServerCertificateTrust.IsPermittedStatus(forbidden | X509ChainStatusFlags.UntrustedRoot),
                    "other validation errors stay rejected: " + forbidden);

            using (var handler = AiHttpTransport.CreateHandler(true, trust))
            {
                Check(handler.CheckCertificateRevocationList, "manual trust requests native revocation checking");
                var validate = handler.ServerCertificateCustomValidationCallback;
                Check(validate != null && validate(null, original, chain, SslPolicyErrors.RemoteCertificateChainErrors),
                    "manual certificate wired to the actual AI handler");
                Check(!validate(null, rotated, otherChain, SslPolicyErrors.None), "handler enforces exact certificate match");
                Check(handler.UseProxy && !handler.AllowAutoRedirect, "manual trust preserves proxy and redirect controls");
            }
            var pinned = AiHttpTransport.GetClient(false, serverCertificateBase64: encoded);
            Check(ReferenceEquals(pinned, AiHttpTransport.GetClient(false, serverCertificateBase64: encoded)), "same certificate reuses its pool");
            Check(!ReferenceEquals(pinned, AiHttpTransport.GetClient(false)), "clearing certificate returns to normal trust pool");
            Check(!ReferenceEquals(pinned, AiHttpTransport.GetClient(true, serverCertificateBase64: encoded)), "TLS modes keep independent certificate pools");
            Check(!ReferenceEquals(pinned, AiHttpTransport.GetClient(false,
                serverCertificateBase64: Convert.ToBase64String(rotated.Export(X509ContentType.Cert)))), "certificate rotation gets a new pool");
            Throws<AiServiceException>(() => AiHttpTransport.GetClient(false, forLegacy: true, serverCertificateBase64: encoded));
            Throws<AiServiceException>(() => ServerCertificateTrust.FromBase64("not a certificate"));
            // Valid empty PKCS#12 envelope: reject containers before importing any keys.
            // Public test certificates never need a macOS temporary private-key keychain.
            byte[] pfx = { 0x30, 0x16, 0x02, 0x01, 0x03, 0x30, 0x11, 0x06, 0x09, 0x2A, 0x86, 0x48,
                0x86, 0xF7, 0x0D, 0x01, 0x07, 0x01, 0xA0, 0x04, 0x04, 0x02, 0x30, 0x00 };
            Check(X509Certificate2.GetCertContentType(pfx) == X509ContentType.Pfx, "fixture is a PKCS#12 container");
            Throws<AiServiceException>(() => ServerCertificateTrust.FromBase64(Convert.ToBase64String(pfx)));

            string file = Path.Combine(Path.GetTempPath(), "askai-server-" + Guid.NewGuid().ToString("N") + ".crt");
            try
            {
                File.WriteAllText(file, "-----BEGIN CERTIFICATE-----\n" + encoded + "\n-----END CERTIFICATE-----\n");
                Check(ServerCertificateTrust.FromFile(file).Fingerprint == trust.Fingerprint, "single PEM certificate supported");
                File.WriteAllText(file, "-----BEGIN CERTIFICATE-----\n" + encoded + "\n-----END CERTIFICATE-----\n", new System.Text.UTF8Encoding(true));
                Check(ServerCertificateTrust.FromFile(file).Fingerprint == trust.Fingerprint, "Windows UTF-8 BOM PEM supported");
                File.WriteAllBytes(file, original.Export(X509ContentType.Cert));
                Check(ServerCertificateTrust.FromFile(file).Fingerprint == trust.Fingerprint, "DER certificate supported");
                byte[] extraData = new byte[original.RawData.Length + 1];
                Array.Copy(original.RawData, extraData, original.RawData.Length);
                File.WriteAllBytes(file, extraData);
                Throws<AiServiceException>(() => ServerCertificateTrust.FromFile(file));
                File.WriteAllText(file, "-----BEGIN CERTIFICATE-----\n" + encoded + "\n-----END CERTIFICATE-----\n" +
                    "-----BEGIN CERTIFICATE-----\n" + encoded + "\n-----END CERTIFICATE-----\n");
                Throws<AiServiceException>(() => ServerCertificateTrust.FromFile(file));
                File.WriteAllBytes(file, new byte[65537]);
                Throws<AiServiceException>(() => ServerCertificateTrust.FromFile(file));
            }
            finally { File.Delete(file); }
        }

        using (var key = RSA.Create(2048))
        using (var expired = CreateCertificate(key, DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddDays(-1), true))
        using (var future = CreateCertificate(key, DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(5), true))
        using (var clientOnly = CreateCertificate(key, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(5), false))
        {
            Throws<AiServiceException>(() => ServerCertificateTrust.FromBase64(Convert.ToBase64String(expired.Export(X509ContentType.Cert))));
            Throws<AiServiceException>(() => ServerCertificateTrust.FromBase64(Convert.ToBase64String(future.Export(X509ContentType.Cert))));
            Throws<AiServiceException>(() => ServerCertificateTrust.FromBase64(Convert.ToBase64String(clientOnly.Export(X509ContentType.Cert))));
        }
        Console.WriteLine("Server certificate checks passed (exact match, names, validity, EKU, chain restrictions, DER/PEM, pool isolation).");
    }

    private static X509Certificate2 CreateCertificate(RSA key, DateTimeOffset start, DateTimeOffset end, bool server)
    {
        var request = new CertificateRequest("CN=mail.example.invalid", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection
        {
            new Oid(server ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2")
        }, true));
        // Create a signed public certificate without attaching/persisting the test private key.
        return request.Create(request.SubjectName, X509SignatureGenerator.CreateForRSA(key, RSASignaturePadding.Pkcs1),
            start, end, Guid.NewGuid().ToByteArray());
    }

    private static X509Chain BuildChain(X509Certificate2 certificate)
    {
        var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.Build(certificate);
        return chain;
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); }
}
