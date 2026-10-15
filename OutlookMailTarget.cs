using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Askai;
using Outlook = Microsoft.Office.Interop.Outlook;

namespace Ask.ai
{
    /// <summary>
    /// Captures the initiating window and selection before any popup opens. All calls are
    /// made on Outlook's UI thread. This adapter only edits/displays drafts; it cannot send.
    /// </summary>
    internal sealed class OutlookMailTarget : IAiDraftTarget, IDisposable
    {
        private readonly Outlook.Application application;
        private readonly MailCapability capability;
        private readonly ComReferences references = new ComReferences();
        private Outlook.MailItem sourceMail;
        private Outlook.Inspector sourceInspector;
        private dynamic document;
        private string bodySnapshot;
        private string documentTextSnapshot;
        private string selectedText;
        private int selectionStart;
        private int selectionEnd;
        private bool closed;
        private bool subscribed;
        private string preparedText;
        private FormattedMailContent preparedContent;
        private const int MaxMessageBytes = 50 * 1024 * 1024;
        private const int MaxDocumentCharacters = 128000;
        private const int MaxHtmlCharacters = 1024 * 1024;
        private const int MaxRtfBytes = 4 * 1024 * 1024;
        public string Subject { get; private set; } = "";
        public string Source { get; private set; } = "";
        public bool ApplyMarkdown { get; set; }
        public bool SupportsRichText => capability == MailCapability.Compose || sourceMail.BodyFormat != Outlook.OlBodyFormat.olFormatPlain;

        private OutlookMailTarget(Outlook.Application application, MailCapability capability)
        {
            this.application = application;
            this.capability = capability;
        }

        public static OutlookMailTarget Capture(Outlook.Application application, object context, MailCapability capability)
        {
            var target = new OutlookMailTarget(application, capability);
            try
            {
                if (capability != MailCapability.Compose) target.CaptureSource(context);
                return target;
            }
            catch { target.Dispose(); throw; }
        }

        public void SetNewSubject(string subject)
        {
            if (capability != MailCapability.Compose) throw new InvalidOperationException();
            Subject = subject;
        }

        private void CaptureSource(object context)
        {
            sourceInspector = context as Outlook.Inspector;
            if (sourceInspector != null)
                sourceMail = references.Keep(sourceInspector.CurrentItem) as Outlook.MailItem;
            else
            {
                if (capability == MailCapability.Improve)
                    throw new MailActionException("Taslağı ayrı bir Outlook penceresinde açın ve iyileştirilecek metni seçin.");
                var explorer = context as Outlook.Explorer;
                if (explorer == null) throw new MailActionException("Bu işlem için Outlook mail penceresini kullanın.");
                var selection = (Outlook.Selection)references.Keep(explorer.Selection);
                if (selection.Count != 1) throw new MailActionException("Lütfen tek bir e-posta seçin.");
                sourceMail = references.Keep(selection[1]) as Outlook.MailItem;
            }
            if (sourceMail == null) throw new MailActionException("Seçili öğe bir e-posta değil.");
            RequireMessageSize();
            Subject = sourceMail.Subject ?? "";
            if (Subject.Length > 255) throw SourceTooLarge();
            if (sourceInspector != null)
            {
                ((Outlook.InspectorEvents_10_Event)sourceInspector).Close += OnInspectorClosed;
                subscribed = true;
            }
            if (capability == MailCapability.Improve) CaptureSelection();
            else
            {
                if (!sourceMail.Sent || sourceMail.Submitted)
                    throw new MailActionException("Yanıt hazırlamak için alınmış veya gönderilmiş bir mail seçin. Taslak için 'Seçili metni iyileştir' kullanın.");
                Source = "Gönderen: " + (sourceMail.SenderName ?? "") + "\n\n" + (sourceMail.Body ?? "");
                if (Source.Length > MailContentLimits.SourceCharacters) throw SourceTooLarge();
            }
            bodySnapshot = ReadBodySnapshot(sourceMail);
        }

        private void CaptureSelection()
        {
            if (sourceMail.Sent || sourceMail.Submitted)
                throw new MailActionException("Yalnızca gönderilmemiş, açık taslaklar iyileştirilebilir.");
            document = references.Keep(GetWordDocument(sourceInspector));
            using (var local = new ComReferences())
            {
                // Scope the selection to this inspector's Word window, never ActiveDocument.
                dynamic windows = local.Keep(document.Windows);
                dynamic window = local.Keep(windows[1]);
                dynamic selection = local.Keep(window.Selection);
                if ((int)selection.Type != 2) // wdSelectionNormal: reject block/table/object selections.
                    throw new MailActionException("İyileştirilecek normal metin aralığını seçin.");
                dynamic range = local.Keep(selection.Range);
                if ((int)range.End - (int)range.Start > MailContentLimits.SourceCharacters + 2) throw SourceTooLarge();
                ValidateTextRange(document, range);
                int startOffset, endOffset;
                selectedText = MailText.SelectionContent((string)range.Text, out startOffset, out endOffset);
                selectionStart = (int)range.Start + startOffset;
                selectionEnd = (int)range.Start + endOffset;
                if (selectionEnd <= selectionStart || string.IsNullOrWhiteSpace(selectedText))
                    throw new MailActionException("İyileştirilecek metni Outlook editöründe seçin.");
                Source = selectedText;
                dynamic content = local.Keep(document.Content);
                RequireDocumentSize(content);
                documentTextSnapshot = (string)content.Text;
            }
        }

        public bool IsCurrent()
        {
            if (closed) return false;
            if (capability == MailCapability.Compose) return true;
            try
            {
                RequireMessageSize();
                if ((sourceMail.Subject ?? "") != Subject || ReadBodySnapshot(sourceMail) != bodySnapshot) return false;
                if (capability != MailCapability.Improve) return !sourceMail.Submitted;
                if (sourceMail.Sent || sourceMail.Submitted || sourceInspector == null) return false;
                using (var local = new ComReferences())
                {
                    object currentMail = local.Keep(sourceInspector.CurrentItem);
                    object currentDocument = local.Keep(GetWordDocument(sourceInspector));
                    if (!ReferenceEquals(currentMail, sourceMail) || !ReferenceEquals(currentDocument, (object)document)) return false;
                    dynamic content = local.Keep(document.Content);
                    RequireDocumentSize(content);
                    if ((string)content.Text != documentTextSnapshot) return false;
                    dynamic range = local.Keep(document.Range(selectionStart, selectionEnd));
                    ValidateTextRange(document, range);
                    return (string)range.Text == selectedText;
                }
            }
            catch (COMException) { return false; }
            catch (InvalidComObjectException) { return false; }
            catch (MailActionException) { return false; }
        }

        public void Apply(string text)
        {
            // Render completely before any Outlook mutation. Keep the raw response when disabled.
            FormattedMailContent formatted = ApplyMarkdown
                ? (text == preparedText ? preparedContent : null) ?? MailFormatting.Create(text, true) : null;
            if (formatted != null) formatted.EnsureVisibleText();
            if (capability == MailCapability.Improve)
            {
                sourceInspector.Activate();
                // Activation can run other add-ins' callbacks. Recheck before the only write.
                if (!IsCurrent())
                    throw new MailActionException("Taslak değişti veya kapatıldı. İçerik aktarılmadı; yeniden hazırlayın.");
                using (var local = new ComReferences())
                {
                    dynamic range = local.Keep(document.Range(selectionStart, selectionEnd));
                    ApplySelection(local, range, text, formatted);
                }
                return;
            }
            CreateDraft(text, formatted);
        }

        internal void SetPreparedContent(string text, FormattedMailContent content)
        {
            preparedText = text;
            preparedContent = content;
        }

        private void ApplySelection(ComReferences local, dynamic range, string text, FormattedMailContent formatted)
        {
            dynamic word = local.Keep(document.Application);
            dynamic undo = local.Keep(word.UndoRecord);
            if ((bool)undo.IsRecordingCustomRecord)
                throw new MailActionException("Outlook editöründe başka bir düzenleme devam ediyor. Biraz sonra yeniden deneyin.");
            string originalSelection = SelectionFingerprint();
            undo.StartCustomRecord("AI Asistan metin iyileştirme");
            bool recording = true;
            try
            {
                WriteSelection(local, (object)range, text, formatted);
                undo.EndCustomRecord();
                recording = false;
            }
            catch
            {
                try
                {
                    if (recording) undo.EndCustomRecord();
                    // Inspect live Word content, including formatting. Outlook's cached body
                    // can lag behind a partial COM write. Subject/Inspector changes alone
                    // must never cause Undo to consume the user's preceding edit.
                    if (SelectionChanged(originalSelection)
                        && (!(bool)document.Undo(1) || SelectionChanged(originalSelection)))
                        throw new InvalidOperationException();
                }
                catch
                {
                    throw new MailActionException("Aktarım tamamlanamadı ve önceki içerik otomatik geri yüklenemedi. Taslak kısmen değişmiş olabilir; Outlook’ta içeriği ve Geri Al seçeneğini kontrol edin.");
                }
                throw;
            }
        }

        private string SelectionFingerprint()
        {
            using (var local = new ComReferences())
            {
                dynamic range = local.Keep(document.Range(selectionStart, selectionEnd));
                string xml = (string)range.WordOpenXML;
                if (xml == null || xml.Length > MaxRtfBytes) throw SourceTooLarge();
                using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(xml)));
            }
        }

        private bool SelectionChanged(string originalSelection)
        {
            using (var local = new ComReferences())
            {
                dynamic content = local.Keep(document.Content);
                RequireDocumentSize(content);
                if ((string)content.Text != documentTextSnapshot) return true;
            }
            return SelectionFingerprint() != originalSelection;
        }

        private void WriteSelection(ComReferences local, dynamic range, string text, FormattedMailContent formatted)
        {
            // Do not replace HTMLBody: only this range is editable by the capability.
            if (formatted == null || !SupportsRichText)
            {
                range.Text = MailText.ForWord(formatted == null ? text : formatted.PlainText).Trim('\r', '\v');
                return;
            }
            var prefix = CaptureRetainedParagraph(local, (object)document, selectionStart, selectionEnd, true);
            var suffix = CaptureRetainedParagraph(local, (object)document, selectionStart, selectionEnd, false);
            dynamic before = local.Keep(document.Content);
            int originalEnd = (int)before.End;
            InsertFormatted(range, formatted.Rtf);
            dynamic after = local.Keep(document.Content);
            int delta = (int)after.End - originalEnd;
            if (prefix != null) RestoreRetainedParagraph(local, (object)document, prefix, 0);
            if (suffix != null) RestoreRetainedParagraph(local, (object)document, suffix, delta);
        }

        private void CreateDraft(string text, FormattedMailContent formatted)
        {
            using (var local = new ComReferences())
            {
                Outlook.MailItem draft;
                if (capability == MailCapability.Compose)
                {
                    draft = (Outlook.MailItem)local.Keep(application.CreateItem(Outlook.OlItemType.olMailItem));
                }
                else
                    draft = (Outlook.MailItem)local.Keep(capability == MailCapability.ReplyAll ? sourceMail.ReplyAll() : sourceMail.Reply());

                try
                {
                    if (capability == MailCapability.Compose)
                    {
                        draft.Subject = Subject;
                        if (formatted != null) draft.BodyFormat = Outlook.OlBodyFormat.olFormatHTML;
                    }
                    // Display first so Outlook inserts the user's configured signature.
                    // Do not Save/Send or replace the existing HTML, recipients, attachments or quote.
                    draft.Display(false);
                    if (draft.Sent || draft.Submitted)
                        throw new MailActionException("Açılan mail artık düzenlenemiyor. Aktarım yapılmadı.");
                    var inspector = (Outlook.Inspector)local.Keep(draft.GetInspector);
                    dynamic editor = local.Keep(GetWordDocument(inspector));
                    dynamic start = local.Keep(editor.Range(0, 0));
                    if (formatted != null && SupportsRichText && draft.BodyFormat == Outlook.OlBodyFormat.olFormatPlain)
                        throw new MailActionException("Açılan mail düz metin biçiminde. Biçimlendirilmiş içerik aktarılmadı; mail biçimini kontrol edin.");
                    if (formatted != null && SupportsRichText && draft.BodyFormat != Outlook.OlBodyFormat.olFormatPlain)
                    {
                        var retained = CaptureRetainedParagraph(local, (object)editor, 0, 0, false);
                        dynamic before = local.Keep(editor.Content);
                        int originalEnd = (int)before.End;
                        InsertFormatted(start, formatted.Rtf.Substring(0, formatted.Rtf.Length - 1)
                            + "\\par\\pard\\plain\\f0\\fs20\\par }");
                        if (retained != null)
                        {
                            dynamic after = local.Keep(editor.Content);
                            RestoreRetainedParagraph(local, (object)editor, retained, (int)after.End - originalEnd);
                        }
                    }
                    else start.Text = MailText.ForWord(formatted == null ? text : formatted.PlainText) + "\r\r";
                }
                catch
                {
                    // Only discard the new, operation-owned draft. Never close or delete the source mail.
                    try
                    {
                        if (!draft.Sent && !draft.Submitted) draft.Close(Outlook.OlInspectorClose.olDiscard);
                    }
                    catch (COMException)
                    {
                        throw new MailActionException("İçerik aktarılamadı ve yeni açılan taslak kapatılamadı. Lütfen bu taslağı Outlook’ta kontrol edin.");
                    }
                    throw;
                }
            }
        }

        private static void InsertFormatted(dynamic range, string rtf)
        {
            string path = Path.Combine(Path.GetTempPath(), "Askai-" + Guid.NewGuid().ToString("N") + ".rtf");
            try
            {
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, Encoding.ASCII)) writer.Write(rtf);
                // No field link, attachment, clipboard or replacement of the entire mail body.
                range.InsertFile(path, Type.Missing, false, false, false);
            }
            finally
            {
                // Cleanup failure after a successful insertion must not report a failed write
                // or discard a newly created draft. Never log the path or mail contents.
                try { File.Delete(path); }
                catch (IOException) { System.Diagnostics.Trace.TraceWarning("AI Asistan geçici RTF dosyası temizlenemedi."); }
                catch (UnauthorizedAccessException) { System.Diagnostics.Trace.TraceWarning("AI Asistan geçici RTF dosyası temizlenemedi."); }
            }
        }

        private static RetainedParagraph CaptureRetainedParagraph(ComReferences local, object editorDocument,
            int start, int end, bool prefix)
        {
            dynamic editor = editorDocument;
            int position = prefix ? start : Math.Max(start, end - 1);
            dynamic anchor = local.Keep(editor.Range(position, position));
            dynamic paragraphs = local.Keep(anchor.Paragraphs);
            dynamic paragraph = local.Keep(paragraphs[1]);
            dynamic range = local.Keep(paragraph.Range);
            // An unselected terminal paragraph mark alone must not suppress AI block formatting.
            bool hasRetainedText = prefix ? start > (int)range.Start : end < (int)range.End - 1;
            if (!hasRetainedText) return null;
            dynamic format = local.Keep(range.ParagraphFormat);
            dynamic list = local.Keep(range.ListFormat);
            bool numbered = (int)list.ListType != 0;
            return new RetainedParagraph
            {
                Anchor = prefix ? start - 1 : end,
                Format = local.Keep(format.Duplicate),
                ListTemplate = numbered ? local.Keep(list.ListTemplate) : null,
                ListLevel = numbered ? (int)list.ListLevelNumber : 0
            };
        }

        private static void RestoreRetainedParagraph(ComReferences local, object editorDocument, RetainedParagraph saved, int delta)
        {
            dynamic editor = editorDocument;
            dynamic anchor = local.Keep(editor.Range(saved.Anchor + delta, saved.Anchor + delta));
            dynamic list = local.Keep(anchor.ListFormat);
            if (saved.ListTemplate == null) list.RemoveNumbers();
            else
                // wdListApplyToSelection=2, wdWord9ListBehavior=1. Never restyle the whole original list.
                list.ApplyListTemplateWithLevel(saved.ListTemplate, true, 2, 1, saved.ListLevel);
            anchor.ParagraphFormat = (dynamic)saved.Format;
        }

        private sealed class RetainedParagraph
        {
            public int Anchor;
            public object Format;
            public object ListTemplate;
            public int ListLevel;
        }

        private static object GetWordDocument(Outlook.Inspector inspector)
        {
            if (!inspector.IsWordMail() || inspector.EditorType != Outlook.OlEditorType.olEditorWord)
                throw new MailActionException("Bu Outlook editörü desteklenmiyor. Maili normal düzenleme penceresinde açın.");
            return inspector.WordEditor;
        }

        private static string ReadBodySnapshot(Outlook.MailItem mail)
        {
            byte[] bytes;
            string format;
            switch (mail.BodyFormat)
            {
                case Outlook.OlBodyFormat.olFormatHTML:
                    string html = mail.HTMLBody ?? "";
                    if (html.Length > MaxHtmlCharacters) throw SourceTooLarge();
                    bytes = Encoding.UTF8.GetBytes(html);
                    format = "html:";
                    break;
                case Outlook.OlBodyFormat.olFormatRichText:
                    bytes = (byte[])mail.RTFBody ?? new byte[0];
                    if (bytes.Length > MaxRtfBytes) throw SourceTooLarge();
                    format = "rtf:";
                    break;
                default:
                    string body = mail.Body ?? "";
                    if (body.Length > MaxDocumentCharacters) throw SourceTooLarge();
                    bytes = Encoding.UTF8.GetBytes(body);
                    format = "text:";
                    break;
            }
            // Retain a fixed-size digest rather than a second full HTML string or Base64 RTF.
            using (var hash = SHA256.Create()) return format + Convert.ToBase64String(hash.ComputeHash(bytes));
        }

        private void RequireMessageSize()
        {
            if (sourceMail.Size > MaxMessageBytes) throw SourceTooLarge();
        }

        private static void RequireDocumentSize(dynamic content)
        {
            if ((int)content.End - (int)content.Start > MaxDocumentCharacters) throw SourceTooLarge();
        }

        private static MailActionException SourceTooLarge()
        {
            return new MailActionException("Mail bu işlem için çok büyük. Kaynak metni 32.000 karakterle sınırlayın veya ilgili bölümü daha küçük, büyük ekler içermeyen bir taslakta seçin. İçerik kesilmedi ve mail değiştirilmedi.");
        }

        private static void ValidateTextRange(dynamic editor, dynamic range)
        {
            // Word constants are named here to avoid a new Word PIA dependency.
            const int mainTextStory = 1;
            const int withInTable = 12;
            const int noProtection = -1;
            using (var local = new ComReferences())
            {
                dynamic tables = local.Keep(range.Tables);
                dynamic images = local.Keep(range.InlineShapes);
                dynamic fields = local.Keep(range.Fields);
                dynamic controls = local.Keep(range.ContentControls);
                if ((int)range.StoryType != mainTextStory || (int)editor.ProtectionType != noProtection
                    || (bool)range.Information[withInTable] || (int)tables.Count > 0 || (int)images.Count > 0
                    || (int)fields.Count > 0 || (int)controls.Count > 0)
                    throw UnsupportedSelection();
                dynamic shapes = local.Keep(editor.Shapes);
                for (int index = 1; index <= (int)shapes.Count; index++)
                {
                    using (var shapeScope = new ComReferences())
                    {
                        dynamic shape = shapeScope.Keep(shapes[index]);
                        dynamic anchor = shapeScope.Keep(shape.Anchor);
                        if ((bool)anchor.InRange(range)) throw UnsupportedSelection();
                    }
                }
            }
        }

        private static MailActionException UnsupportedSelection()
        {
            return new MailActionException("Yalnızca düz metin seçin. Tablo, görsel, alan veya korumalı içerik içeren seçimler iyileştirilemez.");
        }

        private void OnInspectorClosed() { closed = true; }

        public void Dispose()
        {
            if (subscribed)
            {
                try { ((Outlook.InspectorEvents_10_Event)sourceInspector).Close -= OnInspectorClosed; }
                catch (COMException) { /* The inspector may already have been destroyed. */ }
                catch (InvalidComObjectException) { }
                subscribed = false;
            }
            references.Dispose();
            document = null;
            sourceMail = null;
            sourceInspector = null;
            bodySnapshot = null;
            documentTextSnapshot = null;
            selectedText = null;
            preparedText = null;
            preparedContent = null;
        }
    }

    /// <summary>Releases only COM references acquired by this operation, never the shared Application or event context.</summary>
    internal sealed class ComReferences : IDisposable
    {
        private readonly List<object> owned = new List<object>();
        public object Keep(object value)
        {
            if (value != null && Marshal.IsComObject(value)) owned.Add(value);
            return value;
        }
        public void Dispose()
        {
            for (int index = owned.Count - 1; index >= 0; index--)
            {
                try { Marshal.ReleaseComObject(owned[index]); }
                catch (InvalidComObjectException) { /* Outlook can disconnect objects when a window closes. */ }
            }
            owned.Clear();
        }
    }
}
