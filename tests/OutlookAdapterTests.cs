using System;
using Ask.ai;
using Askai;
using Outlook = Microsoft.Office.Interop.Outlook;

internal static class OutlookAdapterTests
{
    public static void Run()
    {
        var app = new Outlook.Application();
        using (var target = OutlookMailTarget.Capture(app, null, MailCapability.Compose))
        {
            target.SetNewSubject("Project update");
            var session = new MailGenerationSession();
            session.Complete("Prepared message");
            Check(app.Created.Count == 0, "no mail exists before approval");
            session.ApplyApproved(target);
            Check(app.Created.Count == 1, "approval creates one new mail");
            var draft = app.Created[0];
            Check(draft.Subject == "Project update" && draft.To == "", "topic copied, recipients left empty");
            Check(draft.Body == "Prepared message\r\rSignature\r" && draft.Displayed, "display/signature precedes insertion");
        }

        var source = new Outlook.MailItem(app) { Subject = "Draft", Body = "Before\rOld paragraph\rSignature\r" };
        var inspector = source.GetInspector;
        inspector.Document.Select(7, 21); // Includes the old paragraph's terminating mark.
        using (var target = OutlookMailTarget.Capture(app, inspector, MailCapability.Improve))
        {
            Check(target.Source == "Old paragraph", "only selected text goes to AI");
            inspector.Document.Select(0, 6); // Moving the cursor must not retarget the result.
            Approve(target, "New text");
            Check(source.Body == "Before\rNew text\rSignature\r", "initial range and signature boundary preserved");
        }

        foreach (string change in new[] { "body", "word-only", "subject", "sent", "submitted", "closed", "identity" })
        {
            source = new Outlook.MailItem(app) { Subject = "Draft", Body = "Original\rSignature\r" };
            inspector = source.GetInspector;
            inspector.Document.Select(0, 8);
            if (change == "word-only") source.BodyFormat = Outlook.OlBodyFormat.olFormatHTML;
            using (var target = OutlookMailTarget.Capture(app, inspector, MailCapability.Improve))
            {
                if (change == "body" || change == "word-only") source.Body = "User edited\rSignature\r";
                if (change == "subject") source.Subject = "User's new subject";
                if (change == "sent") source.Sent = true;
                if (change == "submitted") source.Submitted = true;
                if (change == "closed") inspector.RaiseClose();
                if (change == "identity") inspector.CurrentItem = new Outlook.MailItem(app);
                string before = source.Body;
                Throws<MailActionException>(() => Approve(target, "Must not apply"));
                Check(source.Body == before, "changed/closed/sent target left untouched: " + change);
            }
        }

        source = new Outlook.MailItem(app) { Body = "Original\r" };
        inspector = source.GetInspector;
        inspector.Document.Select(0, 8);
        using (var target = OutlookMailTarget.Capture(app, inspector, MailCapability.Improve))
        {
            inspector.FailActivate = true;
            Throws<System.Runtime.InteropServices.COMException>(() => Approve(target, "Replacement"));
            Check(source.Body == "Original\r", "activation failure occurs before mutation");
        }

        foreach (string kind in new[] { "table", "image", "field", "control", "shape", "block", "empty", "protected" })
        {
            source = new Outlook.MailItem(app) { Body = "Selected text\r" };
            inspector = source.GetInspector;
            inspector.Document.Select(0, 13);
            inspector.Document.Unsupported = kind;
            Throws<MailActionException>(() =>
            {
                using (OutlookMailTarget.Capture(app, inspector, MailCapability.Improve)) { }
            });
        }

        foreach (var capability in new[] { MailCapability.Reply, MailCapability.ReplyAll })
        {
            source = new Outlook.MailItem(app) { Subject = "First", Body = "Original question", Sent = true };
            var explorer = new Outlook.Explorer();
            explorer.Selection.Items.Add(source);
            using (var target = OutlookMailTarget.Capture(app, explorer, capability))
            {
                explorer.Selection.Items.Clear();
                explorer.Selection.Items.Add(new Outlook.MailItem(app) { Subject = "Other", Sent = true });
                Approve(target, "Answer");
                var reply = app.Created[app.Created.Count - 1];
                Check(reply.Subject == "RE: First" && reply.Body == "Answer\r\rSignature\rQuote: Original question",
                    "reply uses captured source and preserves quote");
                Check(reply.To == (capability == MailCapability.ReplyAll ? "all-original-recipients" : "original-sender"),
                    "native reply recipient choice preserved");
            }
        }

        foreach (string failure in new[] { "display", "editor", "insert" })
        {
            app.NextFailure = failure;
            using (var target = OutlookMailTarget.Capture(app, null, MailCapability.Compose))
            {
                Throws<Exception>(() => Approve(target, "Prepared content"));
                Check(app.Created[app.Created.Count - 1].Discarded, "failed new draft discarded: " + failure);
            }
        }
        Console.WriteLine("Outlook adapter simulations passed (captured range, stale target, reply, draft cleanup). Real COM still requires Windows.");
    }

    private static void Approve(IAiDraftTarget target, string result)
    {
        var session = new MailGenerationSession();
        session.Complete(result);
        session.ApplyApproved(target);
    }
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
}
