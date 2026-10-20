using Verse;

namespace ConsumptionRates.Utility
{
    // Named Logger, not Log - Verse.Log is in scope in every file here.
    //
    // Follows the 2026-09-20 logging standard (CLAUDE.md section 4): log the
    // event the mod creates, log when it ends and why, log the intermediate
    // detail needed to answer "why did it do that." Nothing here fires per
    // tick or per frame - every call site is gated on a map load, a map
    // removal, a daily summary, or a player click. See
    // cr-architecture.md section 11 for which call sits where and why.
    public static class Logger
    {
        private const string Prefix = "[ConsumptionRates] ";

        // the one load line
        public static void Loaded(string text)
        {
            Log.Message(Prefix + text);
        }

        // Internal diagnostic / lifecycle line - log only, no on-screen
        // message. Map sampling started/stopped, the daily summary, a
        // toggle changed, the window opening.
        public static void Info(string text)
        {
            Log.Message(Prefix + text);
        }
    }
}
