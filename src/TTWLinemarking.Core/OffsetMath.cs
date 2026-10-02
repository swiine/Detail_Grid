namespace TTWLinemarking.Core
{
    public static class OffsetMath
    {
        // Distance from the centreline reference to each painted line's own centreline.
        //
        // The RMS diagrams dimension the CLEAR gap between painted edges. Each line's paint extends
        // half its width either side of its own centreline, so centre-to-centre is
        // gap + width1/2 + width2/2, and the two lines sit symmetrically either side of the
        // reference at half of that.
        public static double HalfSpacing(double gap, double width1, double width2) =>
            (gap + width1 / 2.0 + width2 / 2.0) / 2.0;

        public static double HalfSpacing(LinemarkingItem item)
        {
            var partner = Catalogue.Find(item.Pair.PartnerCode);
            return HalfSpacing(item.Pair.Gap, item.Width, partner.Width);
        }
    }
}
