using System.Collections.Generic;
using System.Linq;

namespace TTWLinemarking.Core
{
    // One step of the Guided Questions wizard: a question and its answers. Each answer either asks
    // a further question or lands on a linetype code.
    public sealed class Question
    {
        public string Text { get; init; }
        public IReadOnlyList<Answer> Answers { get; init; }
    }

    public sealed class Answer
    {
        public string Text { get; init; }
        public Question Next { get; init; }   // set for a follow-up question...
        public string Code { get; init; }     // ...or this, for a final answer
    }

    public static class GuidedQuestions
    {
        // PLACEHOLDER: category -> item drill-down built from the catalogue. The real question wording
        // is being written from the RMS/TfNSW standards. When it arrives, replace this with a
        // hand-written tree. The wizard renders whatever tree is here; it needs no changes.
        public static Question Root { get; } = new Question
        {
            Text = "What kind of line is this?",
            Answers = Catalogue.Categories
                .Where(cat => Catalogue.Items.Any(i => i.Category == cat))
                .Select(cat => new Answer
                {
                    Text = cat,
                    Next = new Question
                    {
                        Text = "Which of these matches best?",
                        Answers = Catalogue.Items
                            .Where(i => i.Category == cat)
                            .Select(i => new Answer { Text = $"{i.Label}   ({i.Code})", Code = i.Code })
                            .ToList(),
                    },
                })
                .ToList(),
        };
    }
}
