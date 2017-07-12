using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Kinect;
using System.IO;

namespace KSIM.Readers
{
    public abstract class Reader
    {
        public abstract Frame read(MultiSourceFrame f);
    }

    // All frame types start internally from 1
    // i.e. if a client sends all zeros that is invalid frame type
    // The corresponding bit pattern that client should send for requesting a stream is computed as 2**frame_type
    public enum FrameType { Color=1, Skeleton, Audio, Depth, ClosestBody, LHDepth, RHDepth, HeadDepth };

    public static class FrameTypeExtensions
    {
        private static ClosestBodyReader cbr = new ClosestBodyReader();
        private static DepthReader dr = new DepthReader();
        private static LHDepthReader lhd = new LHDepthReader();
        private static RHDepthReader rhd = new RHDepthReader();
        private static HeadDepthReader hdr = new HeadDepthReader();

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
                default:
                    throw new NotImplementedException("Non-implemented reader requested");
            }
        }
    }

    public abstract class Frame : IDisposable
    {
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

        // Note that serialization will follow little-endian format, even for network transfers
        // so write clients accordingly
        public abstract void Serialize(Stream stream);
        
        // Disposable pattern starts
        protected bool disposed = false; // To detect redundant calls

        protected abstract void Dispose(bool disposing);

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        // Disposable pattern ends

    }
}
