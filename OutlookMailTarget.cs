using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
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
            Subject = sourceMail.Subject ?? "";
            bodySnapshot = ReadBodySnapshot(sourceMail);
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
            }
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
                ValidateTextRange(document, range);
                int startOffset, endOffset;
                selectedText = MailText.SelectionContent((string)range.Text, out startOffset, out endOffset);
                selectionStart = (int)range.Start + startOffset;
                selectionEnd = (int)range.Start + endOffset;
                if (selectionEnd <= selectionStart || string.IsNullOrWhiteSpace(selectedText))
                    throw new MailActionException("İyileştirilecek metni Outlook editöründe seçin.");
                Source = selectedText;
                dynamic content = local.Keep(document.Content);
                documentTextSnapshot = (string)content.Text;
            }
        }

        public bool IsCurrent()
        {
            if (closed) return false;
            if (capability == MailCapability.Compose) return true;
            try
            {
                if ((sourceMail.Subject ?? "") != Subject || ReadBodySnapshot(sourceMail) != bodySnapshot) return false;
                if (capability != MailCapability.Improve) return !sourceMail.Submitted;
                if (sourceMail.Sent || sourceMail.Submitted || sourceInspector == null) return false;
                using (var local = new ComReferences())
                {
                    object currentMail = local.Keep(sourceInspector.CurrentItem);
                    object currentDocument = local.Keep(GetWordDocument(sourceInspector));
                    if (!ReferenceEquals(currentMail, sourceMail) || !ReferenceEquals(currentDocument, (object)document)) return false;
                    dynamic content = local.Keep(document.Content);
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
            FormattedMailContent formatted = ApplyMarkdown ? MailFormatting.Create(text, true) : null;
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
                    // Do not replace HTMLBody: only this range is editable by the capability.
                    if (formatted != null && SupportsRichText)
                    {
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
                    else range.Text = MailText.ForWord(formatted == null ? text : formatted.PlainText).Trim('\r', '\v');
                }
                return;
            }
            CreateDraft(text, formatted);
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
            finally { if (File.Exists(path)) File.Delete(path); }
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
            switch (mail.BodyFormat)
            {
                case Outlook.OlBodyFormat.olFormatHTML: return "html:" + mail.HTMLBody;
                case Outlook.OlBodyFormat.olFormatRichText: return "rtf:" + Convert.ToBase64String((byte[])mail.RTFBody);
                default: return "text:" + mail.Body;
            }
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
