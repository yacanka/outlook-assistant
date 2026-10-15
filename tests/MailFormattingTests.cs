using System;
using Askai;

internal static class MailFormattingTests
{
    public static void WriteFixtures(string folder)
    {
        System.IO.Directory.CreateDirectory(folder);
        var sources = new System.Collections.Generic.Dictionary<string, string>
        {
            ["inline"] = "# Başlık\n\n**Kalın** ve *italik* ve ~~sil~~\nAlt satır 😀",
            ["lists"] = "3. Üç\n4. Dört\n   - İç madde\n\nSon paragraf",
            ["table"] = "| Ad | Değer |\n| --- | ---: |\n| **Bir** | 2 |\n\nSon paragraf",
            ["uneven"] = "| A | B |\n| --- | --- |\n| X | Y | Z |\n| Only |",
            ["quote"] = "> Alıntı\n\nSon paragraf",
            ["links"] = "[Site](https://example.invalid) ![Logo](https://example.invalid/a.png)",
            ["code"] = "`{x}`\n\n```text\nC:\\temp\n**literal**\n```"
        };
        foreach (var pair in sources)
            System.IO.File.WriteAllText(System.IO.Path.Combine(folder, pair.Key + ".rtf"), MailFormatting.Create(pair.Value, true).Rtf);
    }

    public static void Run()
    {
        Check(MailText.ForPreview("a\r\nb\nc\rd\vlast") == "a\r\nb\r\nc\r\nd\r\nlast",
            "preview preserves every kind of line break");
        var stream = new MailPreviewBuffer();
        Check(stream.Append("First\r") == "First\r\n", "standalone CR is visible immediately");
        Check(stream.Append("\n\nSecond") == "\r\nSecond", "stream returns only new visible text and does not double a split CRLF");
        Check(stream.Source == "First\r\n\nSecond", "stream retains unmodified source");

        var formatted = MailFormatting.Create("# Başlık\n\n**Kalın** ve *italik* ve ~~sil~~\nAlt satır", true);
        Check(formatted.PlainText == "Başlık\n\nKalın ve italik ve sil\nAlt satır", "readable text and soft breaks");
        Check(formatted.Rtf.Contains("\\b ") && formatted.Rtf.Contains("\\i ")
            && formatted.Rtf.Contains("\\strike "), "inline formatting reaches RTF");
        Check(formatted.Rtf.Contains("\\fs32"), "heading has a distinct size");
        var list = MailFormatting.Create("3. Üç\n4. Dört\n   - İç madde\n\n> Alıntı\n\n---", true);
        Check(list.PlainText.Contains("3. Üç") && list.PlainText.Contains("4. Dört")
            && list.PlainText.Contains("  • İç madde") && list.PlainText.Contains("Alıntı"), "lists preserve order and nesting");
        Check(list.Rtf.Contains("\\listtable") && list.Rtf.Contains("\\ls1"), "lists use native RTF numbering");
        var table = MailFormatting.Create("| Ad | Değer |\n| --- | ---: |\n| **Bir** | 2 |", true);
        Check(table.PlainText == "Ad\tDeğer\nBir\t2", "table fallback retains rows and columns");
        Check(table.Rtf.Contains("\\trowd") && table.Rtf.Contains("\\cellx") && table.Rtf.Contains("\\cell "),
            "table emits actual RTF cells");
        var uneven = MailFormatting.Create("| A | B |\n| --- | --- |\n| X | Y | Z |\n| Only |", true);
        Check(uneven.PlainText == "A\tB\t\nX\tY\tZ\nOnly\t\t", "uneven tables preserve extra cells and pad missing cells");
        var code = MailFormatting.Create("`{x}`\n\n```text\nC:\\temp\n**literal**\n```", true);
        Check(code.PlainText.Contains("**literal**") && code.Rtf.Contains("\\\\temp")
            && code.Rtf.Contains("\\{x\\}"), "code remains literal and RTF escaped");
        var unicode = MailFormatting.Create("Türkçe 😀 \\ { }", false);
        Check(unicode.PlainText == "Türkçe 😀 \\ { }" && unicode.Rtf.Contains("\\u"), "Unicode and braces are safe");
        var links = MailFormatting.Create("[Site](https://example.invalid) ![Logo](https://example.invalid/a.png) [bad](javascript:alert) <script>x</script>", true);
        Check(links.Rtf.Contains("HYPERLINK") && links.PlainText.Contains("Site (https://example.invalid/)")
            && links.PlainText.Contains("Logo") && links.PlainText.Contains("<script>"), "links, image alt and literal HTML retained: " + links.PlainText);
        Check(!links.Rtf.Contains("javascript:") && !links.Rtf.Contains("a.png") && !links.Rtf.Contains("\\pict"),
            "no unsafe links or remote images enter RTF");
        Check(MailFormatting.Create("A &amp; B &#x1F600; \\*literal\\*", true).PlainText == "A & B 😀 *literal*",
            "HTML entities and escaped Markdown retain visible text");
        var disabled = MailFormatting.Create("**Raw**\n\n- Item", false);
        Check(disabled.PlainText == "**Raw**\n\n- Item", "disabled formatting preserves Markdown source");
        Check(MailFormatting.Create("**unfinished", true).PlainText == "**unfinished", "incomplete stream remains readable");
        var prompt = new MailGenerationRequest(MailCapability.Compose, "Subject", "", "", true).BuildPrompt();
        Check(prompt.Contains("Markdown") && !prompt.Contains("Yalnızca düz metin üret"), "Markdown request changes output instructions");
        Console.WriteLine("Mail formatting checks passed (line breaks, Markdown, tables, Unicode, safe RTF).");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
