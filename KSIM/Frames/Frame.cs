using System;
using Microsoft.Kinect;
using Microsoft.Kinect.Face;
using System.IO;
using Microsoft.Speech.Recognition;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace KSIM.Frames
{
    [Flags]
    public enum FrameType
    {
        Color = 2,
        Speech = 4,
        Audio = 8,
        Depth = 16,
        ClosestBody = 32,
        LHDepth = 64,
        RHDepth = 128,
        HeadDepth = 256,
        HeadColor = 512,
        ClosestFace = 1024
    };
    
    public abstract class Frame
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
        

        protected virtual void SerializeHeader(BinaryWriter writer)
        {
            writer.Write(Timestamp);
            writer.Write((int)Type);
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
