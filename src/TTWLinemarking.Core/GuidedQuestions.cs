using System.Collections.Generic;

namespace TTWLinemarking.Core
{
    // One step of the Guided Questions wizard: a question and its answers. Each answer either asks
    // a further question or lands on a linetype code.
    public sealed class Question
    {
        public string Text { get; init; }
        public string Help { get; init; }      // optional extra line under the question
        public IReadOnlyList<Answer> Answers { get; init; }
    }

    public sealed class Answer
    {
        public string Text { get; init; }      // short, plain-language choice
        public string Hint { get; init; }      // smaller "for example..." line under it
        public Question Next { get; init; }    // set for a follow-up question...
        public string Code { get; init; }      // ...or this, for a final answer
    }

    // Written for someone who has never read the delineation manual: describe where the line is
    // and what drivers can do, never the code. Wording is based on TfNSW Delineation Part 4
    // (sections 4.2 dividing, 4.4 barrier, 4.6 lane, 4.7 edge, 4.8 continuity, 4.9 turn lines).
    // The wizard renders whatever tree is here; changing wording needs no form changes.
    public static class GuidedQuestions
    {
        public static Question Root { get; } = Q(
            "Where does the line go?",
            "Pick the closest match. You can go back at any point.",
            Go("Down the middle of a two-way road",
               "Separates traffic going in OPPOSITE directions.", MiddleOfRoad()),
            Go("Between lanes going the same way",
               "e.g. between the two lanes of a dual carriageway, or next to a bus or tram lane.", BetweenLanes()),
            Go("Along the edge of the road, or around an island",
               "e.g. the line beside the shoulder, or the outline of a painted median or splay.", RoadEdge()),
            Go("Where traffic stops or gives way, or turns at an intersection",
               "e.g. a stop line, a give way line, or guide lines through a big intersection.", Intersection()),
            Go("Where people walk across the road",
               "Pedestrian crossings.", Crossing()),
            Go("Along the kerb, for parking or no stopping",
               "e.g. parking bay lines, yellow no stopping or clearway lines.", Kerbside()),
            Go("On an off-road bicycle path",
               "A separate shared path or cycleway, not a bike lane on the road.", BikePath()));

        private static Question MiddleOfRoad() => Q(
            "Can drivers cross this line to overtake?",
            null,
            Go("Yes, from both directions",
               "A normal centre line. Most two-way roads.", DividingLine()),
            Go("Only from one direction",
               "Drivers can't see far enough ahead one way (a crest or curve), or there's a climbing lane.", OneWayBarrier()),
            Go("No, from neither direction",
               "Blind in both directions, or approaching a median island or pedestrian crossing.", Q(
                   "Normal or enhanced lines?",
                   EnhancedHelp,
                   Pick("Normal", "Two solid lines, 0.10 wide.", "BL2"),
                   Pick("Enhanced", "Two solid lines, 0.15 wide.", "BL6"))));

        private const string EnhancedHelp =
            "Enhanced lines are wider. Only use them where the design asks for them. Not sure? Pick Normal.";

        private static Question DividingLine() => Q(
            "Which kind of centre line?",
            "Not sure? Pick the first one.",
            Pick("Normal dividing line", "Broken white line, 0.10 wide. The usual choice.", "DL1"),
            Pick("Enhanced dividing line", "Wider broken line, 0.15. Only where the design asks for it.", "DL4"),
            Pick("Wide centre line (WCL)", "Part of a wide centre line treatment.", "DL4 WCL"));

        private static Question OneWayBarrier() => Q(
            "Normal or enhanced lines?",
            EnhancedHelp,
            Go("Normal", "One broken + one solid line, 0.10 wide.", SidePick("BL1 DASHED", "BL1 SOLID")),
            Go("Enhanced", "One broken + one solid line, 0.15 wide.", SidePick("BL5 DASHED", "BL5 SOLID")));

        // Both answers draw the same pair; they only decide which line lands on the side the
        // drafter clicks after selecting the centreline.
        private static Question SidePick(string dashed, string solid) => Q(
            "After you select the centreline, you'll click a side. Which line goes on that side?",
            "Drivers on the BROKEN-line side may overtake. Drivers on the SOLID-line side may not.",
            Pick("The broken line", "I'll click the side where overtaking IS allowed.", dashed),
            Pick("The solid line", "I'll click the side where overtaking is NOT allowed.", solid));

        private static Question BetweenLanes() => Q(
            "What's on the other side of the line?",
            null,
            Go("Another normal traffic lane",
               "e.g. two lanes heading the same way.", Q(
                   "Can drivers change lanes across it?",
                   null,
                   Pick("Yes", "Broken line. The usual lane line.", "LL1"),
                   Pick("No", "Solid line. No lane changing here.", "LL4"))),
            Go("A special lane (transit lane, T2/T3)",
               "A lane kept for certain vehicles. Not bus or tram lanes, those are separate options.", Q(
                   "Is it on a freeway?",
                   null,
                   Pick("No", "Long broken line, 0.10 wide.", "LL2"),
                   Pick("Yes, a freeway", "Long broken line for freeways.", "LL3"))),
            Go("A bus lane",
               "The red-coloured lines.", Q(
                   "Can other vehicles cross into the bus lane here?",
                   "e.g. to turn left into a driveway or side street.",
                   Pick("Yes", "Broken red line.", "BU1"),
                   Pick("No", "Solid red line.", "BU2"))),
            Go("Tram tracks or a tram lane",
               "The yellow tram lines.", Q(
                   "Which tram line?",
                   null,
                   Pick("Cars and trams share the lane", "Broken yellow line beside tracks in a mixed traffic lane.", "TR1"),
                   Pick("Edge of a tram-only lane", "Solid yellow line.", "TR2"),
                   Pick("Tramway line", "Double solid yellow line.", "TR3"))),
            Go("A turning lane is starting, or a lane is about to end",
               "Short broken line where a through lane runs next to a turn lane.", Q(
                   "What kind of lane is next to the through lane?",
                   null,
                   Pick("A normal turning lane", "Short white dashes.", "CL1"),
                   Pick("A bus lane", "Short red dashes.", "BU4"),
                   Pick("A tram lane", "Short yellow dashes.", "TR4"))));

        private static Question RoadEdge() => Q(
            "Which edge?",
            null,
            Pick("The edge of the road",
                 "Solid line along the left or right edge of the traffic lanes. Includes divided roads and freeways.", "EL1"),
            Pick("Around an island, median, splay or shoulder",
                 "The outline of a painted or raised area. Traffic passes on either side.", "OL1"));

        private static Question Intersection() => Q(
            "What is the line for?",
            null,
            Pick("Stop line", "Solid line where vehicles must stop. There's a STOP sign.", "TF"),
            Go("Give way line", "Broken line where vehicles must give way.", Q(
                "Which side of the road is it on?",
                null,
                Pick("The normal (left) side", "The usual give way line.", "TB"),
                Pick("The right side of the road", "Narrower give way line used on the right-hand side.", "TB1"))),
            Pick("Turning path line", "Short broken line guiding vehicles through a turn.", "T1"),
            Pick("Turn and through paths at a complex intersection",
                 "Short broken lines showing each path through a large or unusual intersection.", "TL"));

        private static Question Crossing() => Q(
            "What kind of crossing?",
            null,
            Pick("Two parallel lines across the road",
                 "Marks the walking area, e.g. at traffic lights. Draws ONE line; you offset the second by hand.", "PCW"),
            Pick("Zebra crossing", "The wide white stripes.", "PX"));

        private static Question Kerbside() => Q(
            "What is the kerb line for?",
            null,
            Pick("Edge of a parking bay", "White line marking out parking spaces.", "PS1"),
            Pick("No stopping, at any time", "Solid yellow line along the kerb.", "NS1"),
            Pick("Clearway (no stopping during set hours)", "Broken yellow line along the kerb.", "NS3"));

        private static Question BikePath() => Q(
            "Where on the path?",
            null,
            Pick("Down the middle, path is straight", "Broken line. Riders can cross it to pass.", "DLP"),
            Pick("Down the middle, riders shouldn't cross", "Solid line, e.g. on bends or narrow sections.", "BLP"),
            Pick("Along the edge of the path", "Solid edge line.", "ELP"));

        private static Question Q(string text, string help, params Answer[] answers) =>
            new Question { Text = text, Help = help, Answers = answers };

        private static Answer Go(string text, string hint, Question next) =>
            new Answer { Text = text, Hint = hint, Next = next };

        private static Answer Pick(string text, string hint, string code) =>
            new Answer { Text = text, Hint = hint, Code = code };
    }
}
