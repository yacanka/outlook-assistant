using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace Askai
{
    public enum AiProvider { Central, Legacy }
    public enum AiRibbonPreset { Standard, RepliesMenu }

    public sealed class AiSettings
    {
        public AiProvider Provider { get; set; } = AiProvider.Central;
        public string Model { get; set; } = "";
        public string Token { get; set; } = "";
        public bool UseTls12 { get; set; }
        public bool ApplyMarkdown { get; set; }
        public AiRibbonPreset RibbonPreset { get; set; } = AiRibbonPreset.Standard;
        /// <summary>Personal context, encrypted for the current Windows user when saved.</summary>
        public string MemoryText { get; set; } = "";
        public bool MemoryEnabled { get; set; } = true;
        public string CentralServerCertificate { get; set; } = "";
        public string CentralCaCertificates { get; set; } = "";
        public string CentralBundleCertificates { get; set; } = "";
        public CertificateTrustMode CentralCertificateTrustMode { get; set; } = CertificateTrustMode.ServerCertificate;
        public string LegacyServerCertificate { get; set; } = "";
        public string LegacyCaCertificates { get; set; } = "";
        public string LegacyBundleCertificates { get; set; } = "";
        public CertificateTrustMode LegacyCertificateTrustMode { get; set; } = CertificateTrustMode.ServerCertificate;
        public string ActiveCentralCertificate => CentralCertificateTrustMode == CertificateTrustMode.CertificateBundle
            ? CentralBundleCertificates : CentralCertificateTrustMode == CertificateTrustMode.CertificateAuthority
                ? CentralCaCertificates : CentralServerCertificate;
        public string ActiveLegacyCertificate => LegacyCertificateTrustMode == CertificateTrustMode.CertificateBundle
            ? LegacyBundleCertificates : LegacyCertificateTrustMode == CertificateTrustMode.CertificateAuthority
                ? LegacyCaCertificates : LegacyServerCertificate;
        private static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Askai", "settings.xml");

        public static AiSettings Load() { return Load(SettingsPath); }

        internal static AiSettings Load(string path)
        {
            if (!File.Exists(path)) return new AiSettings();
            try
            {
                var root = XElement.Load(path);
                AiProvider provider;
                if (!Enum.TryParse((string)root.Element("Provider"), out provider)
                    || !Enum.IsDefined(typeof(AiProvider), provider)) throw new FormatException();
                string encrypted = (string)root.Element("Token") ?? "";
                string encryptedMemory = (string)root.Element("MemoryText") ?? "";
                string memory = encryptedMemory.Length == 0 ? "" : Encoding.UTF8.GetString(ProtectedData.Unprotect(
                    Convert.FromBase64String(encryptedMemory), null, DataProtectionScope.CurrentUser));
                if (memory.Length > AppConfig.MemoryMaxLength) throw new FormatException();
                return new AiSettings {
                    Provider = provider,
                    Model = (string)root.Element("Model") ?? "",
                    UseTls12 = (bool?)root.Element("UseTls12") ?? false,
                    ApplyMarkdown = (bool?)root.Element("ApplyMarkdown") ?? false,
                    RibbonPreset = ParseRibbonPreset((string)root.Element("RibbonPreset")),
                    MemoryEnabled = (bool?)root.Element("MemoryEnabled") ?? true,
                    MemoryText = memory,
                    CentralServerCertificate = (string)root.Element("CentralServerCertificate") ?? "",
                    CentralCaCertificates = (string)root.Element("CentralCaCertificates") ?? "",
                    CentralBundleCertificates = (string)root.Element("CentralBundleCertificates") ?? "",
                    CentralCertificateTrustMode = ServerCertificateTrust.ParseMode((string)root.Element("CentralCertificateTrustMode")),
                    LegacyServerCertificate = (string)root.Element("LegacyServerCertificate") ?? "",
                    LegacyCaCertificates = (string)root.Element("LegacyCaCertificates") ?? "",
                    LegacyBundleCertificates = (string)root.Element("LegacyBundleCertificates") ?? "",
                    LegacyCertificateTrustMode = ServerCertificateTrust.ParseMode((string)root.Element("LegacyCertificateTrustMode")),
                    Token = encrypted.Length == 0 ? "" : Encoding.UTF8.GetString(ProtectedData.Unprotect(
                        Convert.FromBase64String(encrypted), null, DataProtectionScope.CurrentUser))
                };
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                || ex is CryptographicException || ex is FormatException || ex is System.Xml.XmlException)
            {
                throw new InvalidOperationException("AI ayarları okunamadı. AI Asistan ayarlarından yeniden kaydedin.");
            }
        }

        public void Save() { Save(SettingsPath); }

        internal void Save(string path)
        {
            if ((MemoryText ?? "").Length > AppConfig.MemoryMaxLength)
                throw new ArgumentException("Hafıza en fazla 4.000 karakter olabilir. Metni kısaltıp yeniden kaydedin.", nameof(MemoryText));
            string encryptedMemory = string.IsNullOrEmpty(MemoryText) ? "" : Convert.ToBase64String(
                ProtectedData.Protect(Encoding.UTF8.GetBytes(MemoryText), null, DataProtectionScope.CurrentUser));
            string encrypted = string.IsNullOrEmpty(Token) ? "" : Convert.ToBase64String(
                ProtectedData.Protect(Encoding.UTF8.GetBytes(Token), null, DataProtectionScope.CurrentUser));
            var root = new XElement("AiSettings", new XElement("Provider", Provider),
                new XElement("Model", Model), new XElement("Token", encrypted),
                new XElement("UseTls12", UseTls12),
                new XElement("ApplyMarkdown", ApplyMarkdown),
                new XElement("RibbonPreset", RibbonPreset),
                new XElement("MemoryEnabled", MemoryEnabled),
                new XElement("MemoryText", encryptedMemory),
                new XElement("CentralServerCertificate", CentralServerCertificate),
                new XElement("CentralCaCertificates", CentralCaCertificates),
                new XElement("CentralBundleCertificates", CentralBundleCertificates),
                new XElement("CentralCertificateTrustMode", CentralCertificateTrustMode),
                new XElement("LegacyServerCertificate", LegacyServerCertificate),
                new XElement("LegacyCaCertificates", LegacyCaCertificates),
                new XElement("LegacyBundleCertificates", LegacyBundleCertificates),
                new XElement("LegacyCertificateTrustMode", LegacyCertificateTrustMode));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                root.Save(temporary);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static AiRibbonPreset ParseRibbonPreset(string value)
        {
            AiRibbonPreset preset;
            return Enum.TryParse(value, out preset) && Enum.IsDefined(typeof(AiRibbonPreset), preset)
                ? preset : AiRibbonPreset.Standard;
        }
    }
}
