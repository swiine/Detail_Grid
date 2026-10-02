using System.Collections.Generic;
using System.Linq;
using TTWLinemarking.Core;
using Xunit;

namespace TTWLinemarking.Tests
{
    public class ScaleTests
    {
        [Theory]
        [InlineData("1:100", 100)]
        [InlineData("1:200", 200)]
        [InlineData("1:250", 250)]
        [InlineData("1:500", 500)]
        [InlineData("1:1000", 1000)]
        [InlineData(" 1 : 500 ", 500)]
        [InlineData("1:100_XREF", 100)]
        [InlineData("1:1000_XREF", 1000)]
        [InlineData("ttw_scale_1:100m", 100)]
        [InlineData("ttw_scale_1:200m", 200)]
        [InlineData("ttw_scale_1:250m", 250)]
        [InlineData("ttw_scale_1:500m", 500)]
        [InlineData("ttw_scale_1:1000m", 1000)]
        [InlineData("TTW_SCALE_1:500M", 500)]
        public void Supported_scales_parse(string name, int expected)
        {
            Assert.Equal(expected, Scales.Parse(name));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("1:1")]
        [InlineData("1:10")]
        [InlineData("1:50")]
        [InlineData("1:2000")]
        [InlineData("1:10000")]
        [InlineData("2:100")]
        [InlineData("21:100")]
        [InlineData("ttw_scale_1:10m")]
        [InlineData("ttw_scale_1:2000m")]
        [InlineData("1/4\" = 1'-0\"")]
        public void Unsupported_scales_are_rejected(string name)
        {
            Assert.Null(Scales.Parse(name));
        }

        [Fact]
        public void File_names_and_message_text()
        {
            Assert.Equal("TTW_stdState_Linetype_Linemk 250.lin", Scales.LinFileName(250));
            Assert.Equal("1:100, 1:200, 1:250, 1:500 or 1:1000", Scales.SupportedList());
        }
    }

    public class OffsetTests
    {
        [Theory]
        [InlineData("BL1 DASHED", 0.10)]   // BS: 0.10 + 0.10 gap -> centres 0.20 apart
        [InlineData("BL1 SOLID", 0.10)]
        [InlineData("BL2", 0.10)]          // BB
        [InlineData("BL5 DASHED", 0.15)]   // BS1: 0.15 + 0.15 gap -> centres 0.30 apart
        [InlineData("BL6", 0.15)]          // BB1
        public void Each_line_sits_half_the_centre_spacing_from_the_reference(string code, double expected)
        {
            Assert.Equal(expected, OffsetMath.HalfSpacing(Catalogue.Find(code)), 9);
        }

        [Fact]
        public void Unequal_widths_split_evenly()
        {
            // gap 0.2, widths 0.1 and 0.3: centres 0.2 + 0.05 + 0.15 = 0.4 apart, 0.2 each side.
            Assert.Equal(0.2, OffsetMath.HalfSpacing(0.2, 0.1, 0.3), 9);
        }
    }

    public class GuidedQuestionTests
    {
        private static IEnumerable<string> Codes(Question q) =>
            q.Answers.SelectMany(a => a.Next != null ? Codes(a.Next) : new[] { a.Code });

        private static IEnumerable<Answer> AllAnswers(Question q) =>
            q.Answers.SelectMany(a => a.Next != null ? new[] { a }.Concat(AllAnswers(a.Next)) : new[] { a });

        [Fact]
        public void Every_answer_is_a_question_or_a_real_code()
        {
            foreach (var a in AllAnswers(GuidedQuestions.Root))
            {
                Assert.True((a.Next != null) ^ (a.Code != null), a.Text);
                if (a.Code != null) Assert.NotNull(Catalogue.Find(a.Code));
            }
        }

        [Fact]
        public void Every_catalogue_item_is_reachable()
        {
            var reachable = Codes(GuidedQuestions.Root).ToHashSet();
            Assert.All(Catalogue.Items, i => Assert.Contains(i.Code, reachable));
        }
    }

    public class LinFileTests
    {
        [Fact]
        public void Parses_comment_parts_and_pattern()
        {
            const string text =
                ";; header\r\n" +
                "SOME SECTION\r\n" +
                "*BU1,(0.1 wide RED)(RMS Code = X9)Bus lane thing  ____   ____\r\n" +
                "A,90,-30\r\n";
            var e = LinFile.Parse(text).Single();
            Assert.Equal("BU1", e.Name);
            Assert.Equal("SOME SECTION", e.Section);
            Assert.Equal("0.1 wide RED", e.WidthNote);
            Assert.Equal("X9", e.RmsCode);
            Assert.Equal("Bus lane thing", e.Description);
            Assert.Equal(new[] { 90.0, -30.0 }, e.Pattern);
        }
    }
}
