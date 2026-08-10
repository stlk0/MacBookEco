namespace MacBookEco.Core
{
    /// <summary>
    /// Comparison that does not stop at the first difference.
    ///
    /// Both overloads reject differing lengths immediately, because no caller
    /// here treats a length as secret, and then examine every element. What
    /// the timing must not reveal is how much of a durable token, an ownership
    /// hash or an EDID matched before the comparison gave up.
    ///
    /// This is the single definition. The same loop was previously written out
    /// in the digest type, in the watchdog protocol and in two test harnesses.
    /// </summary>
    public static class FixedTimeComparer
    {
        public static bool AreEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;

            int difference = 0;
            for (int index = 0; index < left.Length; index++)
                difference |= left[index] ^ right[index];
            return difference == 0;
        }

        public static bool AreEqual(string left, string right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;

            int difference = 0;
            for (int index = 0; index < left.Length; index++)
                difference |= left[index] ^ right[index];
            return difference == 0;
        }
    }
}
