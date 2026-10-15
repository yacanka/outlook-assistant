// Test-only, in-memory Office boundary. This compiles the real adapter on non-Windows hosts.
// It does NOT emulate COM lifetime, Word formatting, Outlook autosave, or the VSTO ribbon.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Microsoft.Office.Interop.Outlook
{
    public enum OlItemType { olMailItem }
    public enum OlBodyFormat { olFormatPlain, olFormatHTML, olFormatRichText }
    public enum OlEditorType { olEditorWord }
    public enum OlInspectorClose { olDiscard }
    public interface InspectorEvents_10_Event { event Action Close; }

    public sealed class Application
    {
        public readonly List<MailItem> Created = new List<MailItem>();
        public string NextFailure;
        public object CreateItem(OlItemType type)
        {
            var mail = new MailItem(this) { Failure = NextFailure };
            NextFailure = null;
            Created.Add(mail);
            return mail;
        }
    }

    public sealed class MailItem
    {
        private readonly Application owner;
        private readonly Inspector inspector;
        public MailItem(Application owner) { this.owner = owner; inspector = new Inspector(this); }
        public string Subject { get; set; } = "";
        public string To { get; set; } = "";
        public string SenderName => "Synthetic sender";
        public bool Sent { get; set; }
        public bool Submitted { get; set; }
        public OlBodyFormat BodyFormat { get; set; }
        public string Body { get => inspector.Document.Text; set => inspector.Document.Text = value; }
        // Deliberately independent from live Word text to exercise the adapter's second snapshot.
        private string htmlBody = "<p>Original</p>";
        public bool FailRichBodyRead;
        public int RichBodyReads;
        public int ReportedSize;
        public int Size => ReportedSize;
        public string HTMLBody
        {
            get { RichBodyReads++; if (FailRichBodyRead) throw new COMException("Unexpected full body read"); return htmlBody; }
            set { htmlBody = value; }
        }
        public object RTFBody { get { RichBodyReads++; if (FailRichBodyRead) throw new COMException("Unexpected full body read"); return new byte[] { 1, 2, 3 }; } }
        public bool Displayed { get; private set; }
        public bool Discarded { get; private set; }
        public string Failure;
        public Inspector GetInspector => inspector;
        public void Display(bool modal)
        {
            if (Failure == "display") throw new COMException("Synthetic display failure");
            if (!Displayed) Body = "Signature\r" + Body;
            Displayed = true;
        }
        public void Close(OlInspectorClose behavior) { Discarded = true; inspector.RaiseClose(); }
        public MailItem Reply() { return CreateReply(false); }
        public MailItem ReplyAll() { return CreateReply(true); }
        private MailItem CreateReply(bool all)
        {
            var reply = (MailItem)owner.CreateItem(OlItemType.olMailItem);
            reply.Subject = "RE: " + Subject;
            reply.To = all ? "all-original-recipients" : "original-sender";
            reply.Body += "Quote: " + Body;
            return reply;
        }
    }

    public sealed class Inspector : InspectorEvents_10_Event
    {
        private event Action closing;
        private readonly MailItem mail;
        public Inspector(MailItem mail)
        {
            this.mail = mail;
            CurrentItem = mail;
            Document = new OfficeSimulation.Document(() => mail.Failure == "insert");
        }
        public OfficeSimulation.Document Document { get; }
        public object CurrentItem { get; set; }
        public object WordEditor => Document;
        public bool IsWordMail() { return mail.Failure != "editor"; }
        public OlEditorType EditorType => OlEditorType.olEditorWord;
        public bool FailActivate;
        public void Activate() { if (FailActivate) throw new COMException("Synthetic activation failure"); }
        event Action InspectorEvents_10_Event.Close { add { closing += value; } remove { closing -= value; } }
        public void RaiseClose() { closing?.Invoke(); }
    }

    public sealed class Explorer { public Selection Selection { get; } = new Selection(); }
    public sealed class Selection
    {
        public readonly List<object> Items = new List<object>();
        public int Count => Items.Count;
        public object this[int index] => Items[index - 1];
    }
}

namespace OfficeSimulation
{
    public sealed class Items<T>
    {
        public readonly List<T> Values = new List<T>();
        public int Count => Values.Count;
        public T this[int index] => Values[index - 1];
    }
    public sealed class Counted { public int Count { get; set; } }
    public sealed class Document
    {
        public string LastInsertedRtf;
        public string LastInsertPath;
        public int LastInsertStart;
        public int LastInsertEnd;
        public ParagraphFormat ParagraphFormat { get; set; } = new ParagraphFormat();
        public ListFormat ListFormat { get; } = new ListFormat();
        private readonly Func<bool> failInsert;
        public Document(Func<bool> failInsert)
        {
            this.failInsert = failInsert;
            Application = new WordApplication(this);
            Windows.Values.Add(new Window(this));
        }
        public WordApplication Application { get; }
        public bool FailAfterInsert;
        public bool FormatOnlyPartialFailure;
        public Action BeforeInsertionFailure;
        public bool FailRestore;
        public bool FailUndo;
        public int UndoCalls;
        public bool Undo(int count)
        {
            UndoCalls++;
            if (FailUndo) return false;
            return Application.UndoRecord.Restore();
        }
        public string Text { get; set; } = "";
        public string Unsupported;
        public int ProtectionType => Unsupported == "protected" ? 1 : -1;
        public Items<Window> Windows { get; } = new Items<Window>();
        public Items<Shape> Shapes
        {
            get
            {
                var shapes = new Items<Shape>();
                if (Unsupported == "shape") shapes.Values.Add(new Shape { Anchor = Range(1, 1) });
                return shapes;
            }
        }
        public Range Content => Range(0, Text.Length);
        public Range Range(int start, int end)
        {
            if (start < 0 || end < start || end > Text.Length) throw new COMException("Invalid range");
            return new Range(this, start, end, failInsert);
        }
        public void Select(int start, int end) { Windows[1].Selection.Selected = Range(start, end); }
    }
    public sealed class WordApplication
    {
        public WordApplication(Document document) { UndoRecord = new UndoRecord(document); }
        public UndoRecord UndoRecord { get; }
    }
    public sealed class UndoRecord
    {
        private readonly Document document;
        private string originalText;
        private float originalIndent;
        private int originalListType;
        private int originalListLevel;
        public UndoRecord(Document document) { this.document = document; }
        public bool IsRecordingCustomRecord { get; private set; }
        public void StartCustomRecord(string name)
        {
            if (IsRecordingCustomRecord) throw new COMException("Nested record");
            originalText = document.Text;
            originalIndent = document.ParagraphFormat.LeftIndent;
            originalListType = document.ListFormat.ListType;
            originalListLevel = document.ListFormat.ListLevelNumber;
            IsRecordingCustomRecord = true;
        }
        public void EndCustomRecord() { IsRecordingCustomRecord = false; }
        public bool Restore()
        {
            if (originalText == null || IsRecordingCustomRecord) return false;
            document.Text = originalText;
            document.ParagraphFormat.LeftIndent = originalIndent;
            document.ListFormat.ListType = originalListType;
            document.ListFormat.ListLevelNumber = originalListLevel;
            return true;
        }
    }
    public sealed class Window
    {
        public Window(Document document) { Selection = new Selection(document); }
        public Selection Selection { get; }
    }
    public sealed class Selection
    {
        private readonly Document document;
        public Selection(Document document) { this.document = document; }
        public Range Selected;
        public int Type => document.Unsupported == "block" ? 6 : 2;
        public Range Range => document.Unsupported == "empty" ? document.Range(0, 0) : Selected;
    }
    public sealed class Shape { public Range Anchor { get; set; } }
    public sealed class ParagraphFormat
    {
        public float LeftIndent { get; set; }
        public ParagraphFormat Duplicate => new ParagraphFormat { LeftIndent = LeftIndent };
    }
    public sealed class ListFormat
    {
        public int ListType { get; set; }
        public object ListTemplate { get; } = new object();
        public int ListLevelNumber { get; set; } = 1;
        public int LastApplyTo;
        public void RemoveNumbers() { ListType = 0; }
        public void ApplyListTemplateWithLevel(object template, bool continuePrevious, int applyTo, int behavior, int level)
        {
            if (!ReferenceEquals(template, ListTemplate) || applyTo != 2) throw new Exception("Must restore original template only to selected paragraph");
            LastApplyTo = applyTo;
            ListType = 1;
            ListLevelNumber = level;
        }
    }
    public sealed class Paragraph
    {
        public Range Range { get; set; }
    }
    public sealed class Range
    {
        private readonly Document document;
        private readonly Func<bool> failInsert;
        public Range(Document document, int start, int end, Func<bool> failInsert)
        {
            this.document = document; Start = start; End = end; this.failInsert = failInsert;
        }
        public int Start { get; }
        public int End { get; }
        public int StoryType => 1;
        public Counted Tables => new Counted { Count = document.Unsupported == "table" ? 1 : 0 };
        public Counted InlineShapes => new Counted { Count = document.Unsupported == "image" ? 1 : 0 };
        public Counted Fields => new Counted { Count = document.Unsupported == "field" ? 1 : 0 };
        public Counted ContentControls => new Counted { Count = document.Unsupported == "control" ? 1 : 0 };
        public Dictionary<int, bool> Information => new Dictionary<int, bool> { [12] = document.Unsupported == "table" };
        public bool InRange(Range other) { return Start >= other.Start && End <= other.End; }
        public Items<Paragraph> Paragraphs
        {
            get
            {
                int start = Start == 0 ? 0 : document.Text.LastIndexOf('\r', Start - 1) + 1;
                int end = document.Text.IndexOf('\r', Start);
                var paragraphs = new Items<Paragraph>();
                paragraphs.Values.Add(new Paragraph { Range = document.Range(start, end < 0 ? document.Text.Length : end + 1) });
                return paragraphs;
            }
        }
        public ParagraphFormat ParagraphFormat
        {
            get => document.ParagraphFormat;
            set
            {
                if (document.FailRestore) { document.FailRestore = false; throw new COMException("Synthetic paragraph restore failure"); }
                document.ParagraphFormat = value.Duplicate;
            }
        }
        public string WordOpenXML => new System.Xml.Linq.XElement("selection",
            new System.Xml.Linq.XAttribute("indent", document.ParagraphFormat.LeftIndent),
            new System.Xml.Linq.XAttribute("list", document.ListFormat.ListType),
            new System.Xml.Linq.XAttribute("level", document.ListFormat.ListLevelNumber), Text).ToString();
        public ListFormat ListFormat => document.ListFormat;
        public void InsertFile(string path, object range, bool confirmConversions, bool link, bool attachment)
        {
            if (confirmConversions || link || attachment) throw new Exception("Unsafe RTF import options");
            document.LastInsertedRtf = System.IO.File.ReadAllText(path);
            document.LastInsertPath = path;
            document.LastInsertStart = Start;
            document.LastInsertEnd = End;
            // Record the real adapter's COM arguments. This deliberately does not emulate RTF layout.
            if (!document.FormatOnlyPartialFailure) Text = "[Imported rich content]";
            // Import may change shared paragraph properties, even when its text range is narrow.
            document.ParagraphFormat.LeftIndent = 0;
            document.ListFormat.ListType = 0;
            if (document.FailAfterInsert || document.FormatOnlyPartialFailure) throw new COMException("Synthetic failure after partial insertion");
        }
        public string Text
        {
            get => document.Text.Substring(Start, End - Start);
            set
            {
                if (failInsert()) { document.BeforeInsertionFailure?.Invoke(); throw new COMException("Synthetic insertion failure"); }
                document.Text = document.Text.Substring(0, Start) + value + document.Text.Substring(End);
            }
        }
    }
}
