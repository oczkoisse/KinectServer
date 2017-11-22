﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Kinect;
using System.Diagnostics;
using System.Collections.Concurrent;
using Microsoft.Speech.Recognition;

namespace KSIM.Readers
{
    class SpeechReader : Reader
    {
        // Singleton for SpeechReader
        private static SpeechReader instance = new SpeechReader();

        private static ConcurrentQueue<SpeechFrame> listSpeechFrame = new ConcurrentQueue<SpeechFrame>(); 
        
        private SpeechReader()
        {

        }

        public static SpeechReader Instance()
        {
            return instance;
        }

        public override Frame Read(MultiSourceFrame f)
        {
            // Thread safe to call Read from multiple threads
            SpeechFrame outFrame = new SpeechFrame(), tempFrame;
            
            while(listSpeechFrame.TryDequeue(out tempFrame))
            {
                outFrame += tempFrame;
            }
            return outFrame;
        }

        public void Store(RecognitionResult r)
        {
            listSpeechFrame.Enqueue(new SpeechFrame(r));
        }
    }

    class SpeechFrame : Frame
    {
        private bool disposed = false;

        private string command = "";

        private bool HasData
        {
            get { return command.Length > 0; }
        }

        private static String[] recognizedKeys = new String[] { "direction", "property", "demonstrative", "other", "action" };
        private static double phraseConfidence = 0.3;
        private static double keyConfidence = 0.1;

        public SpeechFrame(RecognitionResult r)
        {
            this.Type = FrameType.Speech;

            Debug.WriteLine("Phrase confidence: " + r.Semantics.Confidence);

            if (r.Confidence >= phraseConfidence)
            {
                foreach (String k in recognizedKeys)
                {
                    if (r.Semantics.ContainsKey(k))
                    {
                        Debug.WriteLine("Key confidence: " + r.Semantics[k].Confidence);
                        if (r.Semantics[k].Confidence >= keyConfidence)
                        {
                            command = r.Semantics[k].Value.ToString();
                            Debug.WriteLine(command);
                        }
                        break;
                    }
                }                
            }
        }

        public SpeechFrame()
        {
            this.Type = FrameType.Speech;
        }

        public static SpeechFrame operator +(SpeechFrame a, SpeechFrame b)
        {
            SpeechFrame a_plus_b = new SpeechFrame();
            if (a.HasData && b.HasData)
                a_plus_b.command = a.command + ", " + b.command;
            else if (a.HasData)
                a_plus_b.command = a.command;
            else
                a_plus_b.command = b.command;
   
            return a_plus_b;
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
