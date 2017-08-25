using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Kinect;
using System.Diagnostics;
using Microsoft.Speech.Recognition;

namespace KSIM.Readers
{
    class SpeechReader : Reader
    {
        public override Frame Read(MultiSourceFrame f)
        {
            throw new NotImplementedException("SpeechReader cannot read from a MultiSourceFrame. Pass RecognitionResult instead.");
        }

        public override Frame Read(RecognitionResult r)
        {
            var sf = new SpeechFrame(r);
            if (sf != null && sf.HasData)
                return sf;
            else
                sf.Dispose();

            return null;
        }
    }

    class SpeechFrame : Frame
    {
        private bool disposed = false;

        private bool hasData = false;
        public bool HasData
        {
            get { return hasData; }
        }

        private string command;

        public SpeechFrame(RecognitionResult r)
        {
            this.Type = FrameType.Speech;
            
            if (r.Confidence >= 0.3)
            {
                command = r.Semantics.Value.ToString();
                Debug.Write(command + "\n");
                hasData = true;
            }
        }
           
        public override void Serialize(Stream s)
        {
            using (BinaryWriter writer = new BinaryWriter(s))
            {
                int loadSize = 0;
                writer.Write(loadSize);
                writer.Write(Timestamp);
                writer.Write(1 << (int)Type);

                byte[] dataToBeWritten = Encoding.ASCII.GetBytes(command);
                writer.Write(dataToBeWritten.Length);
                writer.Write(dataToBeWritten);

                // Rewind back to write the load size in the first 4 bytes
                loadSize = (int)writer.Seek(0, SeekOrigin.Current) - sizeof(int);
                writer.Seek(0, SeekOrigin.Begin);
                writer.Write(loadSize);
                writer.Seek(0, SeekOrigin.End);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                }
            }
            disposed = true;
        }
    }
}
