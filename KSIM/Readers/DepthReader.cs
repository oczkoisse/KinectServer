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
        public override Frame Read(MultiSourceFrame f)
        {
            // Note that we do not dispose the acquired frame
            // that responsibility is delegated to newly created frame
            var originalFrame = f.DepthFrameReference.AcquireFrame();
            
            if (originalFrame == null)
                return null;
            else
                return new DepthFrame(originalFrame);
        }
    }

    public class DepthFrame : Frame
    {
        private bool disposed = false;

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
            return IndexIntoDepthData((int)x, (int)y);
        }

        protected int IndexIntoDepthData(int x, int y)
        {
            // Need to check the boundaries of the actual frame to be sure we are returning a valid index
            // Note that it allows one off the end boundaries for x (width)
            // This allows you to write loops in the usual "less than length" fashion
            // when applying an operation row wise.
            // However the bound is strictly enforced in y direction, since there may not
            // be a use case for applying an operation column wise
            if (x >= 0 && x <= Width && y >= 0 && y < Height)
                return y * Width + x;
            else
                return -1;
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
            // Load Size (4 bytes, signed) | Timestamp (8 bytes, signed) | Frame Type (4 bytes, bitset) | Width (4 bytes, signed) | Height (4 bytes, signed) | Depth Data (2 bytes * Width * Height, unsigned)
            // Description:
            // Load Size: Number of bytes to be read further to read one DepthFrame
            // Type: The type of the stream
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
                writer.Write(1 << (int)Type);
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
                    // dispose managed state (managed objects)
                    if (underlyingDepthFrame != null)
                        underlyingDepthFrame.Dispose();
                }
            }
            disposed = true;
        }
    }
}
