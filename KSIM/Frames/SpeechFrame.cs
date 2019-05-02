using System.IO;
using System.Text;
using Microsoft.Kinect;
using System.Diagnostics;
using System.Collections.Concurrent;
using Microsoft.Speech.Recognition;

namespace KSIM.Frames
{
    class SpeechFrame : Frame
    {
        private const string _commandDelimiter = "/";

        private string command = "";

        private bool HasData
        {
            get { return command.Length > 0; }
        }

        private const double phraseConfidence = 0.3;

        public SpeechFrame(RecognitionResult r)
        {
            this.Type = FrameType.Speech;

            Debug.WriteLine("Phrase \"{0}\" (confidence: {1})", r.Text, r.Confidence);
			
            // temporary stopgap to re-use voxsim side input symbols for the PDA
            if ("never mind".Equals(r.Text))
            {
                command = string.Format("{0} {1}", r.Semantics["Tag"].Value, r.Text);
            }
            else if (r.Text.Contains(" "))
            {
                command = string.Format("{0},{1}", r.Semantics["Tag"].Value, r.Text);
            }
            else
            {
                command = string.Format("{0} {1}", r.Semantics["Tag"].Value, r.Text);
            }
            Debug.WriteLine(command);
			
        }

        public SpeechFrame()
        {
            this.Type = FrameType.Speech;
        }

        public static SpeechFrame operator +(SpeechFrame a, SpeechFrame b)
        {
            SpeechFrame a_plus_b = new SpeechFrame();
            if (a.HasData && b.HasData)
                a_plus_b.command = a.command + _commandDelimiter + b.command;
            else if (a.HasData)
                a_plus_b.command = a.command;
            else
                a_plus_b.command = b.command;
   
            return a_plus_b;
        }

        protected override void SerializeMiddle(BinaryWriter writer)
        {
            byte[] dataToBeWritten = Encoding.ASCII.GetBytes(command);
            writer.Write(dataToBeWritten.Length);
            writer.Write(dataToBeWritten);
        }
    }
}
