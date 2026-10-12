using System;
using System.Globalization;
using System.Text;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Askai
{
    internal sealed class FormattedMailContent
    {
        public string PlainText { get; }
        public string Rtf { get; }
        public FormattedMailContent(string plainText, string rtf) { PlainText = plainText; Rtf = rtf; }
        public void EnsureVisibleText()
        {
            if (string.IsNullOrWhiteSpace(PlainText))
                throw new MailActionException("Yapay zeka görünür mail metni döndürmedi. Mail değiştirilmedi.");
        }
    }

    /// <summary>Retains the original stream and normalizes its complete text, including split CRLF pairs.</summary>
    internal sealed class MailPreviewBuffer
    {
        private readonly StringBuilder source = new StringBuilder();
        public string Source => source.ToString();
        public string Append(string chunk)
        {
            source.Append(chunk);
            return MailText.ForPreview(source.ToString());
        }
        public void Replace(string text) { source.Clear(); source.Append(text); }
    }

    internal static class MailFormatting
    {
        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .DisableHtml().UsePipeTables().UseEmphasisExtras(Markdig.Extensions.EmphasisExtras.EmphasisExtraOptions.Strikethrough)
            .Build();

        public static FormattedMailContent Create(string source, bool applyMarkdown, int tableWidthTwips = 8640)
        {
            var renderer = new MailRtfRenderer(tableWidthTwips);
            string text = (source ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Replace('\v', '\n');
            if (applyMarkdown) renderer.Render(Markdown.Parse(text, Pipeline));
            else renderer.WriteLiteral(text);
            return renderer.Complete();
        }
    }

    /// <summary>
    /// Emits only application-owned RTF controls. Markdown is never accepted as HTML, RTF,
    /// an image, or an embedded object. The same result is used by RichEdit and Word.
    /// </summary>
    internal sealed class MailRtfRenderer
    {
        private readonly StringBuilder body = new StringBuilder();
        private readonly StringBuilder plain = new StringBuilder();
        private readonly StringBuilder lists = new StringBuilder();
        private readonly StringBuilder overrides = new StringBuilder();
        private int listCount;
        private bool hasBlock;
        private readonly int tableWidthTwips;

        public MailRtfRenderer(int tableWidthTwips) { this.tableWidthTwips = Math.Max(1440, tableWidthTwips); }

        public void WriteLiteral(string text) { WriteText(text); }

        public FormattedMailContent Complete()
        {
            string header = "{\\rtf1\\ansi\\deff0\\uc1{\\fonttbl{\\f0 Segoe UI;}{\\f1 Consolas;}}";
            if (listCount > 0) header += "{\\*\\listtable" + lists + "}{\\*\\listoverridetable" + overrides + "}";
            return new FormattedMailContent(plain.ToString(), header + "\\pard\\plain\\f0\\fs20 " + body + "}");
        }

        public void Render(ContainerBlock container, int indent = 0)
        {
            foreach (Block block in container) RenderBlock(block, indent);
        }

        private void BeginBlock(int indent, string style = "", bool compact = false)
        {
            if (hasBlock)
            {
                plain.Append(compact ? "\n" : "\n\n");
                body.Append(compact ? "\\par " : "\\par\\pard\\plain\\f0\\fs20\\par ");
            }
            hasBlock = true;
            body.Append("\\pard\\plain\\f0\\fs20\\li").Append(indent * 360).Append(style).Append(' ');
        }

        private void RenderBlock(Block block, int indent)
        {
            var list = block as ListBlock;
            if (list != null) { RenderList(list, indent); return; }
            var table = block as Table;
            if (table != null) { RenderTable(table, indent); return; }
            var quote = block as QuoteBlock;
            if (quote != null) { Render(quote, indent + 1); return; }
            var heading = block as HeadingBlock;
            if (heading != null)
            {
                BeginBlock(indent, "\\b\\fs" + (34 - Math.Min(heading.Level, 6) * 2));
                RenderInlines(heading.Inline);
                return;
            }
            var code = block as CodeBlock;
            if (code != null)
            {
                BeginBlock(indent, "\\f1");
                WriteText(code.Lines.ToString());
                return;
            }
            if (block is ThematicBreakBlock)
            {
                BeginBlock(indent, "\\brdrb\\brdrs\\brdrw10");
                WriteText("────────");
                return;
            }
            var paragraph = block as ParagraphBlock;
            if (paragraph != null)
            {
                BeginBlock(indent);
                RenderInlines(paragraph.Inline);
            }
            // Link reference definitions have no visible body. Other enabled nodes are handled above.
        }

        private int DefineList(ListBlock list)
        {
            int id = ++listCount;
            int start;
            if (!int.TryParse(list.OrderedStart, NumberStyles.None, CultureInfo.InvariantCulture, out start)) start = 1;
            // One definition per list keeps ordered starts and nested restarts independent.
            lists.Append("{\\list\\listtemplateid").Append(id).Append("\\listsimple1{\\listlevel\\levelnfc")
                .Append(list.IsOrdered ? 0 : 23).Append("\\leveljc0\\levelfollow0\\levelstartat").Append(start)
                .Append(list.IsOrdered ? "{\\leveltext\\'02\\'00.;}{\\levelnumbers\\'01;}" : "{\\leveltext\\'01\\u8226 ?;}{\\levelnumbers;}")
                .Append("\\fi-360\\li720}\\listid").Append(id).Append('}');
            overrides.Append("{\\listoverride\\listid").Append(id).Append("\\listoverridecount0\\ls").Append(id).Append('}');
            return id;
        }

        private void RenderList(ListBlock list, int indent)
        {
            int id = DefineList(list);
            int number;
            if (!int.TryParse(list.OrderedStart, NumberStyles.None, CultureInfo.InvariantCulture, out number)) number = 1;
            bool first = true;
            foreach (ListItemBlock item in list)
            {
                string marker = list.IsOrdered ? number.ToString(CultureInfo.InvariantCulture) + "." : "•";
                BeginBlock(indent + 2, "\\fi-360\\ls" + id + "\\ilvl0", !first || indent > 0);
                plain.Append(new string(' ', indent * 2)).Append(marker).Append(' ');
                body.Append("{\\listtext ");
                AppendEscaped(body, marker);
                body.Append("\\tab }");
                bool firstParagraph = true;
                foreach (Block child in item)
                {
                    var paragraph = child as ParagraphBlock;
                    if (paragraph != null && firstParagraph)
                    {
                        RenderInlines(paragraph.Inline);
                        firstParagraph = false;
                    }
                    else
                    {
                        RenderBlock(child, indent + 1);
                    }
                }
                first = false;
                number++;
            }
        }

        private void RenderTable(Table table, int indent)
        {
            if (hasBlock) { plain.Append("\n\n"); body.Append("\\par\\pard\\plain\\f0\\fs20\\par "); }
            hasBlock = true;
            int columns = table.ColumnDefinitions == null ? 0 : table.ColumnDefinitions.Count;
            foreach (TableRow row in table) columns = Math.Max(columns, row.Count);
            if (columns == 0) return;
            bool firstRow = true;
            foreach (TableRow row in table)
            {
                if (!firstRow) plain.Append('\n');
                body.Append("{\\trowd\\trgaph90\\trleft").Append(indent * 360);
                for (int column = 1; column <= columns; column++)
                    body.Append("\\clbrdrt\\brdrs\\brdrw10\\clbrdrl\\brdrs\\brdrw10\\clbrdrb\\brdrs\\brdrw10\\clbrdrr\\brdrs\\brdrw10\\cellx")
                        .Append(indent * 360 + column * Math.Max(1, (tableWidthTwips - indent * 360) / columns));
                int cellIndex = 0;
                foreach (TableCell cell in row)
                {
                    if (cellIndex > 0) plain.Append('\t');
                    body.Append("{\\pard\\plain\\intbl\\f0\\fs20");
                    if (row.IsHeader) body.Append("\\b");
                    var alignment = table.ColumnDefinitions != null && cellIndex < table.ColumnDefinitions.Count
                        ? table.ColumnDefinitions[cellIndex].Alignment : null;
                    if (alignment == TableColumnAlign.Right) body.Append("\\qr");
                    if (alignment == TableColumnAlign.Center) body.Append("\\qc");
                    body.Append(' ');
                    bool firstParagraph = true;
                    foreach (Block child in cell)
                    {
                        var leaf = child as LeafBlock;
                        if (leaf == null) continue;
                        if (!firstParagraph) WriteText("\n");
                        RenderInlines(leaf.Inline);
                        firstParagraph = false;
                    }
                    body.Append("\\cell }");
                    cellIndex++;
                }
                while (cellIndex++ < columns)
                {
                    plain.Append('\t');
                    body.Append("{\\pard\\plain\\intbl \\cell }");
                }
                body.Append("\\row }");
                firstRow = false;
            }
            body.Append("\\pard\\plain\\f0\\fs20 ");
        }

        private void RenderInlines(ContainerInline container)
        {
            if (container == null) return;
            for (Inline inline = container.FirstChild; inline != null; inline = inline.NextSibling) RenderInline(inline);
        }

        private void RenderInline(Inline inline)
        {
            var literal = inline as LiteralInline;
            if (literal != null) { WriteText(literal.Content.ToString()); return; }
            var entity = inline as HtmlEntityInline;
            if (entity != null) { WriteText(entity.Transcoded.ToString()); return; }
            if (inline is LineBreakInline) { WriteText("\n"); return; }
            var code = inline as CodeInline;
            if (code != null) { body.Append("{\\f1 "); WriteText(code.Content); body.Append('}'); return; }
            var emphasis = inline as EmphasisInline;
            if (emphasis != null)
            {
                string style = emphasis.DelimiterChar == '~' ? "\\strike" : emphasis.DelimiterCount == 2 ? "\\b" : "\\i";
                body.Append('{').Append(style).Append(' ');
                RenderInlines(emphasis);
                body.Append('}');
                return;
            }
            var link = inline as LinkInline;
            if (link != null) { RenderLink(link); return; }
            var auto = inline as AutolinkInline;
            if (auto != null)
            {
                RenderUrl(auto.Url, auto.IsEmail ? "mailto:" + auto.Url : auto.Url);
                return;
            }
            var children = inline as ContainerInline;
            if (children != null) RenderInlines(children);
        }

        private void RenderLink(LinkInline link)
        {
            string url;
            if (link.IsImage || !SafeUrl(link.Url, out url)) { RenderInlines(link); return; }
            body.Append("{\\field{\\*\\fldinst HYPERLINK \"");
            AppendEscaped(body, url);
            body.Append("\"}{\\fldrslt{\\ul ");
            RenderInlines(link);
            body.Append("}}}");
            plain.Append(" (").Append(url).Append(')');
        }

        private void RenderUrl(string label, string target)
        {
            string url;
            if (!SafeUrl(target, out url)) { WriteText(label); return; }
            body.Append("{\\field{\\*\\fldinst HYPERLINK \"");
            AppendEscaped(body, url);
            body.Append("\"}{\\fldrslt{\\ul ");
            WriteText(label);
            body.Append("}}}");
        }

        private static bool SafeUrl(string target, out string url)
        {
            url = null;
            if (string.IsNullOrEmpty(target)) return false;
            foreach (char value in target)
                if (char.IsControl(value) || value == '"' || value == '\\') return false;
            Uri uri;
            if (!Uri.TryCreate(target, UriKind.Absolute, out uri)
                || (uri.Scheme != "https" && uri.Scheme != "http" && uri.Scheme != "mailto")) return false;
            url = uri.AbsoluteUri;
            return true;
        }

        private void WriteText(string text)
        {
            plain.Append(text);
            AppendEscaped(body, text);
        }

        internal static void AppendEscaped(StringBuilder output, string text)
        {
            foreach (char value in text)
            {
                if (value == '\\' || value == '{' || value == '}') output.Append('\\').Append(value);
                else if (value == '\n' || value == '\r' || value == '\v') output.Append("\\line ");
                else if (value == '\t') output.Append("\\tab ");
                else if (value >= 32 && value <= 126) output.Append(value);
                else output.Append("\\u").Append(((short)value).ToString(CultureInfo.InvariantCulture)).Append('?');
            }
        }
    }
}
