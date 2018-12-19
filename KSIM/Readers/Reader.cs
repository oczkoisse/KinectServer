using System;
using Microsoft.Kinect;
using System.IO;
using Microsoft.Speech.Recognition;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace KSIM.Readers
{
    public abstract class Reader
    {
        public abstract Frame Read(MultiSourceFrame f);

        public virtual Frame Read(AudioBeamFrameList lf)
        {
            throw new NotImplementedException("This Reader cannot read from a AudioBeamFrameList. Pass MultiSourceFrame instead.");
        }

        public virtual Frame Read(RecognitionResult lf)
        {
            throw new NotImplementedException("This Reader cannot read from a RecognitionResult. Pass MultiSourceFrame instead.");
        }
    }

    // All frame types start internally from 1
    // i.e. if a client sends all zeros that is invalid frame type
    // The corresponding bit pattern that client should send for requesting a stream is computed as 2**frame_type
    public enum FrameType { Color=1, Speech, Audio, Depth, ClosestBody, LHDepth, RHDepth, HeadDepth };

    public static class FrameTypeExtensions
    {
        private static ClosestBodyReader cbr = new ClosestBodyReader();
        private static DepthReader dr = new DepthReader();
        private static LHDepthReader lhd = new LHDepthReader();
        private static RHDepthReader rhd = new RHDepthReader();
        private static HeadDepthReader hdr = new HeadDepthReader();
        private static AudioReader ar = new AudioReader();
        private static ColorReader cr = new ColorReader();
        private static SpeechReader sr = SpeechReader.Instance();

        public static Reader GetReader(this FrameType ft)
        {
            switch(ft)
            {
                case FrameType.ClosestBody:
                    return cbr;
                case FrameType.Depth:
                    return dr;
                case FrameType.LHDepth:
                    return lhd;
                case FrameType.RHDepth:
                    return rhd;
                case FrameType.HeadDepth:
                    return hdr;
                case FrameType.Audio:
                    return ar;
                case FrameType.Color:
                    return cr;
                case FrameType.Speech:
                    return sr;
                default:
                    throw new NotImplementedException("Non-implemented reader requested");
            }
        }
    }

    public abstract class Frame : IDisposable
    {
        public byte[] Affix;

        private int width = 0, height = 0;

        public int Width
        {
            get { return width; }
            protected set { width = value; }
        }

        public int Height
        {
            get { return height; }
            protected set { height = value; }
        }

        private FrameType ft;

        public FrameType Type
        {
            get { return ft; }
            protected set { ft = value; }
        }

        // No sync by default
        private long timestamp = -1;

        // To allow syncing of different types of frames over network by their timestamp
        public long Timestamp
        {
            get { return timestamp; }
            set { timestamp = value; }
        }

        
        
        // Disposable pattern should always be implemented in base class
        protected abstract void Dispose(bool disposing);

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        // Disposable pattern ends

        protected virtual void SerializeHeader(BinaryWriter writer)
        {
            writer.Write(Timestamp);
            writer.Write(1 << (int)Type);
        }


        protected abstract void SerializeMiddle(BinaryWriter writer);

        protected virtual void SerializeTail(BinaryWriter writer)
        {
            if (Affix != null)
            {
                writer.Write(Affix.Length);
                writer.Write(Affix);
            }
            else
            {
                writer.Write(0);
            }
        }

        // Note that serialization will follow little-endian format, even for network transfers
        // so write clients accordingly
        public void Serialize(Stream stream)
        {
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                SerializeHeader(writer);
                SerializeMiddle(writer);
                SerializeTail(writer);
            }
        }
    }
}
