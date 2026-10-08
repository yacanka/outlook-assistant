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
        public string HTMLBody { get; set; } = "<p>Original</p>";
        public object RTFBody => new byte[] { 1, 2, 3 };
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
        private readonly Func<bool> failInsert;
        public Document(Func<bool> failInsert)
        {
            this.failInsert = failInsert;
            Windows.Values.Add(new Window(this));
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
        public string Text
        {
            get => document.Text.Substring(Start, End - Start);
            set
            {
                if (failInsert()) throw new COMException("Synthetic insertion failure");
                document.Text = document.Text.Substring(0, Start) + value + document.Text.Substring(End);
            }
        }
    }
}
