using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace Askai
{
    public enum AiProvider { Central, Legacy }

    public sealed class AiSettings
    {
        public AiProvider Provider { get; set; } = AiProvider.Central;
        public string Model { get; set; } = "";
        public string Token { get; set; } = "";
        public bool UseTls12 { get; set; }
        public string CentralServerCertificate { get; set; } = "";
        public string CentralCaCertificates { get; set; } = "";
        public CertificateTrustMode CentralCertificateTrustMode { get; set; } = CertificateTrustMode.ServerCertificate;
        public string ActiveCentralCertificate => CentralCertificateTrustMode == CertificateTrustMode.CertificateAuthority
            ? CentralCaCertificates : CentralServerCertificate;
        private static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Askai", "settings.xml");

        public static AiSettings Load()
        {
            if (!File.Exists(SettingsPath)) return new AiSettings();
            try
            {
                var root = XElement.Load(SettingsPath);
                AiProvider provider;
                if (!Enum.TryParse((string)root.Element("Provider"), out provider)
                    || !Enum.IsDefined(typeof(AiProvider), provider)) throw new FormatException();
                string encrypted = (string)root.Element("Token") ?? "";
                return new AiSettings {
                    Provider = provider,
                    Model = (string)root.Element("Model") ?? "",
                    UseTls12 = (bool?)root.Element("UseTls12") ?? false,
                    CentralServerCertificate = (string)root.Element("CentralServerCertificate") ?? "",
                    CentralCaCertificates = (string)root.Element("CentralCaCertificates") ?? "",
                    CentralCertificateTrustMode = ServerCertificateTrust.ParseMode((string)root.Element("CentralCertificateTrustMode")),
                    Token = encrypted.Length == 0 ? "" : Encoding.UTF8.GetString(ProtectedData.Unprotect(
                        Convert.FromBase64String(encrypted), null, DataProtectionScope.CurrentUser))
                };
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                || ex is CryptographicException || ex is FormatException || ex is System.Xml.XmlException)
            {
                throw new InvalidOperationException("AI ayarları okunamadı. AI Ayarları penceresinden yeniden kaydedin.");
            }
        }

        public void Save()
        {
            string encrypted = string.IsNullOrEmpty(Token) ? "" : Convert.ToBase64String(
                ProtectedData.Protect(Encoding.UTF8.GetBytes(Token), null, DataProtectionScope.CurrentUser));
            var root = new XElement("AiSettings", new XElement("Provider", Provider),
                new XElement("Model", Model), new XElement("Token", encrypted),
                new XElement("UseTls12", UseTls12),
                new XElement("CentralServerCertificate", CentralServerCertificate),
                new XElement("CentralCaCertificates", CentralCaCertificates),
                new XElement("CentralCertificateTrustMode", CentralCertificateTrustMode));
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            string temporary = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                root.Save(temporary);
                if (File.Exists(SettingsPath)) File.Replace(temporary, SettingsPath, null);
                else File.Move(temporary, SettingsPath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
