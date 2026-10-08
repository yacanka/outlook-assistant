using System;
using Askai;

internal static class MailGenerationTests
{
    public static void Run()
    {
        var target = new DraftTarget();
        var session = new MailGenerationSession();
        Throws<InvalidOperationException>(() => session.ApplyApproved(target));
        Check(target.Writes == 0, "preparation cannot write a draft");
        Check(session.Complete("Improved message"), "generation completes");
        Check(target.Writes == 0, "completion alone cannot write a draft");
        session.ApplyApproved(target);
        Check(target.Text == "Improved message" && target.Writes == 1, "approval writes completed text");
        Throws<InvalidOperationException>(() => session.ApplyApproved(target));
        Check(target.Writes == 1, "double approval cannot duplicate content");

        session = new MailGenerationSession();
        session.Cancel();
        Check(!session.Complete("Late result"), "late completion after cancel is discarded");
        Throws<InvalidOperationException>(() => session.ApplyApproved(target));

        session = new MailGenerationSession();
        session.Complete("Discard this");
        session.Cancel();
        Throws<InvalidOperationException>(() => session.ApplyApproved(target));
        Check(target.Writes == 1, "declining approval leaves draft unchanged");

        session = new MailGenerationSession();
        Throws<MailActionException>(() => session.Complete(" \r\n"));
        Check(session.State == MailGenerationState.Failed, "empty result fails");
        session = new MailGenerationSession();
        session.Fail();
        Check(!session.Complete("Partial result"), "failure cannot become ready");

        session = new MailGenerationSession();
        session.Complete("Outdated result");
        target.Current = false;
        Throws<MailActionException>(() => session.ApplyApproved(target));
        Check(target.Writes == 1 && session.State == MailGenerationState.Failed,
            "changed, sent or closed draft blocks transfer");
        target.Current = true;
        target.ThrowOnWrite = true;
        session = new MailGenerationSession();
        session.Complete("Result");
        Throws<InvalidOperationException>(() => session.ApplyApproved(target));
        Check(session.State == MailGenerationState.Failed, "failed transfer is terminal");
        Throws<InvalidOperationException>(() => session.ApplyApproved(target));

        Throws<MailActionException>(() => new MailGenerationRequest(MailCapability.Compose, " ", "", "").BuildPrompt());
        Throws<MailActionException>(() => new MailGenerationRequest(MailCapability.Improve, "Subject", " ", "").BuildPrompt());
        Throws<MailActionException>(() => new MailGenerationRequest(MailCapability.Reply, "Subject", new string('x', 32001), "").BuildPrompt());
        var compose = new MailGenerationRequest(MailCapability.Compose, "Launch", "", "Explain the next steps").BuildPrompt();
        var improve = new MailGenerationRequest(MailCapability.Improve, "Launch", "We meet on Friday.", "").BuildPrompt();
        var reply = new MailGenerationRequest(MailCapability.ReplyAll, "Launch", "Can we meet?", "Decline politely").BuildPrompt();
        Check(compose.Contains("Launch") && compose.Contains("Explain the next steps"), "compose includes topic and instructions");
        Check(improve.Contains("We meet on Friday.") && improve != compose, "improvement uses selected source");
        Check(reply.Contains("Can we meet?") && reply.Contains("Decline politely") && reply != improve,
            "reply includes original context and user intent");
        Check(MailText.ForWord("a\r\nb\nc\rd") == "a\rb\rc\rd", "normalize Word paragraph breaks");
        int start, end;
        Check(MailText.SelectionContent("Original paragraph\r", out start, out end) == "Original paragraph"
            && start == 0 && end == 18, "signature paragraph mark stays outside replacement");
        Check(MailText.SelectionContent("\r\rFirst\rSecond\r\r", out start, out end) == "First\rSecond"
            && start == 2 && end == 14, "both outer boundaries preserved but internal paragraphs remain editable");
        Check(MailText.SelectionContent("\vSelected\v", out start, out end) == "Selected"
            && start == 1 && end == 9, "soft line boundaries preserved");
        Check(MailText.SelectionContent("\r\r", out start, out end) == "" && start == end,
            "boundary-only selection cannot rewrite surrounding text");
        Console.WriteLine("Mail generation checks passed (approval, cancellation, stale target, failure, prompts).");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    private sealed class DraftTarget : IAiDraftTarget
    {
        public bool Current = true;
        public bool ThrowOnWrite;
        public int Writes;
        public string Text = "Original message";
        public bool IsCurrent() { return Current; }
        public void Apply(string text)
        {
            if (ThrowOnWrite) throw new InvalidOperationException("Unavailable editor");
            Writes++;
            Text = text;
        }
    }
}
