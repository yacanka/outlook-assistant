using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Askai
{
    /// <summary>A user-authored instruction preset; no mail content is learned automatically.</summary>
    public sealed class AiSkill
    {
        public string Name { get; }
        public string Instructions { get; }
        public AiSkill(string name, string instructions)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80
                || string.IsNullOrWhiteSpace(instructions) || instructions.Length > 4000)
                throw new ArgumentException("Beceri adı 1–80, talimatı 1–4.000 karakter olmalıdır.");
            Name = name.Trim();
            Instructions = instructions;
        }
        public override string ToString() => Name;
    }

    internal static class AiSkillCatalog
    {
        internal static List<AiSkill> Defaults() => new List<AiSkill>
        {
            new AiSkill("Nazikçe reddet", "Talebi nazikçe reddet. Bilinmeyen gerekçe veya taahhüt ekleme."),
            new AiSkill("Eksik bilgi iste", "İlerlemek için gereken eksik bilgileri kısa ve açık sorularla iste."),
            new AiSkill("Durum güncellemesi yaz", "Verilen bilgilerle kısa bir durum güncellemesi yaz. Tamamlanan işler, bekleyenler ve sonraki adımları belirt; bilinmeyen durumları uydurma."),
            new AiSkill("Gecikmeyi bildir", "Gecikmeyi nazikçe bildir. Yalnızca verilen gerekçe ve tarihleri kullan; yeni teslim tarihi uydurma.")
        };

        internal static string Serialize(IList<AiSkill> skills)
        {
            if (skills == null || skills.Count > 30) throw new ArgumentException("En fazla 30 beceri kaydedilebilir.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var root = new XElement("Skills");
            foreach (var skill in skills)
            {
                if (skill == null || !names.Add(skill.Name)) throw new ArgumentException("Beceri adları benzersiz olmalıdır.");
                root.Add(new XElement("Skill", new XElement("Name", skill.Name), new XElement("Instructions", skill.Instructions)));
            }
            return root.ToString(SaveOptions.DisableFormatting);
        }

        internal static List<AiSkill> Deserialize(string text)
        {
            var options = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 };
            using (var reader = XmlReader.Create(new StringReader(text), options))
            {
                var root = XElement.Load(reader);
                if (root.Name != "Skills") throw new FormatException();
                var skills = root.Elements("Skill").Select(item => new AiSkill((string)item.Element("Name"), (string)item.Element("Instructions"))).ToList();
                Serialize(skills);
                return skills;
            }
        }
    }
}
