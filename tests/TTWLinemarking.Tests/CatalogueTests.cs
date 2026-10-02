using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TTWLinemarking.Core;
using Xunit;

namespace TTWLinemarking.Tests
{
    // The catalogue duplicates what's in the shipped .lin files (so the dialog can show it without
    // the network drive). These tests fail if the two ever drift apart.
    public class CatalogueTests
    {
        public static IEnumerable<object[]> AllScales => Scales.Supported.Select(s => new object[] { s });

        private static List<LinEntry> Load(int scale)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "linetypes", Scales.LinFileName(scale));
            return LinFile.Parse(File.ReadAllText(path));
        }

        [Theory]
        [MemberData(nameof(AllScales))]
        public void Lin_file_has_exactly_the_catalogue_codes(int scale)
        {
            var inFile = Load(scale).Select(e => e.Name).OrderBy(n => n).ToList();
            var inCatalogue = Catalogue.Items.Select(i => i.Code).OrderBy(n => n).ToList();
            Assert.Equal(inCatalogue, inFile);
        }

        [Theory]
        [MemberData(nameof(AllScales))]
        public void Lin_text_matches_catalogue(int scale)
        {
            foreach (var entry in Load(scale))
            {
                var item = Catalogue.Find(entry.Name);
                Assert.True(entry.Description == item.Description, $"{item.Code} description: '{entry.Description}' vs '{item.Description}'");
                Assert.True(entry.RmsCode == item.RmsCode, $"{item.Code} RMS code: '{entry.RmsCode}' vs '{item.RmsCode}'");
                Assert.True(entry.WidthNote == item.StandardNote, $"{item.Code} note: '{entry.WidthNote}' vs '{item.StandardNote}'");
                Assert.True(entry.Section == item.Category, $"{item.Code} section: '{entry.Section}' vs '{item.Category}'");
            }
        }

        [Theory]
        [MemberData(nameof(AllScales))]
        public void Lin_pattern_is_the_1to100_pattern_scaled(int scale)
        {
            foreach (var entry in Load(scale))
            {
                var item = Catalogue.Find(entry.Name);
                if (item.Solid)
                {
                    Assert.Equal(new[] { 1.0, 1.0 }, entry.Pattern);
                    continue;
                }
                Assert.Equal(item.Dash.Length, entry.Pattern.Length);
                for (int k = 0; k < item.Dash.Length; k++)
                    Assert.Equal(item.Dash[k] * 100.0 / scale, entry.Pattern[k], 9);
                Assert.Equal(Catalogue.ExpectedPatternLength(item, scale), entry.Pattern.Sum(Math.Abs), 9);
            }
        }

        [Fact]
        public void Paint_matches_the_colour_in_the_standard_note()
        {
            foreach (var item in Catalogue.Items)
            {
                var expected = item.StandardNote.Contains("RED") ? Paint.Red
                             : item.StandardNote.Contains("YELLOW") ? Paint.Yellow
                             : Paint.White;
                Assert.True(expected == item.Paint, item.Code);
            }
        }

        [Fact]
        public void Double_flag_follows_the_standard_note_except_superseded_DL4()
        {
            // DL4/DL4 WCL: S2 is superseded by S6, a single line (decided in the V2.0.3 review).
            var overridden = new[] { "DL4", "DL4 WCL" };
            foreach (var item in Catalogue.Items)
            {
                bool noteSaysDouble = item.StandardNote.StartsWith("Double");
                bool expected = noteSaysDouble && !overridden.Contains(item.Code);
                Assert.True(expected == item.Double, item.Code);
            }
        }

        [Fact]
        public void Widths_match_the_production_LISP_rule()
        {
            // TTW-GetWidth in ttw_linemarking_V1.9.1.lsp.
            var w030 = new[] { "TF", "TB" };
            var w015 = new[] { "CL1", "EL1", "OL1", "BU4", "TR4", "DL4", "DL4 WCL", "BL5 DASHED", "BL5 SOLID", "BL6", "TB1" };
            foreach (var item in Catalogue.Items)
            {
                double expected = w030.Contains(item.Code) ? 0.30 : w015.Contains(item.Code) ? 0.15 : 0.10;
                Assert.True(expected == item.Width, $"{item.Code}: {item.Width}");
            }
        }

        [Fact]
        public void Pairs_point_at_each_other_with_the_same_gap()
        {
            foreach (var item in Catalogue.Items.Where(i => i.Pair != null))
            {
                var partner = Catalogue.Find(item.Pair.PartnerCode);
                Assert.NotNull(partner);
                Assert.NotNull(partner.Pair);
                Assert.Equal(item.Code, partner.Pair.PartnerCode);
                Assert.Equal(item.Pair.Gap, partner.Pair.Gap);
            }
        }

        [Fact]
        public void Only_PCW_and_TR3_need_a_manual_offset()
        {
            var manual = Catalogue.Items.Where(i => i.NeedsManualOffset).Select(i => i.Code).OrderBy(c => c);
            Assert.Equal(new[] { "PCW", "TR3" }, manual);
        }

        [Fact]
        public void Every_item_is_in_a_known_category_and_codes_are_unique()
        {
            Assert.All(Catalogue.Items, i => Assert.Contains(i.Category, Catalogue.Categories));
            Assert.Equal(Catalogue.Items.Count, Catalogue.Items.Select(i => i.Code.ToUpperInvariant()).Distinct().Count());
            Assert.Equal(36, Catalogue.Items.Count);
        }

        [Fact]
        public void Find_is_case_insensitive_and_null_safe()
        {
            Assert.Same(Catalogue.Find("BL1 DASHED"), Catalogue.Find("bl1 dashed"));
            Assert.Null(Catalogue.Find("NOPE"));
            Assert.Null(Catalogue.Find(null));
        }
    }
}
