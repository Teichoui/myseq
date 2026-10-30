using System.Speech.Synthesis;

namespace Structures
{
    public struct Talker
    {
        public string SpeakingText { get; set; }
        public static SpeechSynthesizer Speech { get; set; } = new SpeechSynthesizer();

        public void SpeakText()
        {
            // Alerts are spoken from worker threads and a synthesizer handles one request at a time.
            lock (Speech)
            {
                Speech.Speak(SpeakingText);
            }
        }
    }
}
