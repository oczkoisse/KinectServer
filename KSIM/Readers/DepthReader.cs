using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Kinect;
using System.IO;
using System.Collections.ObjectModel;

namespace KSIM.Readers
{
    public class DepthReader : Reader
    {
        public override Frame read(MultiSourceFrame f)
        {
            // Note that we do not dispose the acquired frame
            // that responsibility is delegated to newly created frame
            var originalFrame = f.DepthFrameReference.AcquireFrame();
            
            if (originalFrame == null)
                throw new NullReferenceException("Can't retrieve Depth frame");

            return new DepthFrame(originalFrame);
        }
    }

    public class DepthFrame : Frame
    {
        private Microsoft.Kinect.DepthFrame underlyingDepthFrame = null;

        protected Microsoft.Kinect.DepthFrame UnderlyingDepthFrame
        {
            get { return underlyingDepthFrame; }
        }

        protected ushort[] depthData = null;

        public ReadOnlyCollection<ushort> DepthData
        {
            get
            {
                return Array.AsReadOnly(depthData);
            }
        }

        protected int IndexIntoDepthData(float x, float y)
        {
            return (int)y * Width + (int)x;
        }

        protected int IndexIntoDepthData(int x, int y)
        {
            return y * Width + x;
        }


        public DepthFrame(Microsoft.Kinect.DepthFrame df)
        {
            Type = FrameType.Depth;
            this.underlyingDepthFrame = df;
            // Set Dimensions of the depth frame
            Width = df.DepthFrameSource.FrameDescription.Width;
            Height = df.DepthFrameSource.FrameDescription.Height;

            // Reserve memory for storing depth data
            if (depthData == null)
                depthData = new ushort[Width * Height];

            // Copy depth data to memory reserved earlier
            df.CopyFrameDataToArray(this.depthData);
        }

        public override void Serialize(Stream s)
        {
            // Format:
            // Load Size (4 bytes, signed) | Timestamp (8 bytes, signed) | Width (4 bytes, signed) | Height (4 bytes, signed) | Depth Data (2 bytes * Width * Height, unsigned)
            // Description:
            // Load Size: Number of bytes to be read further to read one DepthFrame
            // Timestamp: For syncing frames at the client side
            // Width: Width of the frame
            // Height: Height of the frame
            // Depth Data: Depth data stored as one row following the other

            // Note that BinaryWriter is documented to write data in little-endian form only
            using (BinaryWriter writer = new BinaryWriter(s))
            {
                int loadSize = 0;
                writer.Write(loadSize);

                writer.Write(Timestamp);
                writer.Write(Width);
                writer.Write(Height);

                for (int i = 0; i < depthData.Length; i++)
                    writer.Write(depthData[i]);

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
                    // TODO: dispose managed state (managed objects).
                }
                underlyingDepthFrame.Dispose();
                disposed = true;
            }
        }
    }
}
