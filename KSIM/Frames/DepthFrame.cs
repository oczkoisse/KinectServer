using System;
using Microsoft.Kinect;
using System.IO;
using System.Collections.ObjectModel;

namespace KSIM.Frames
{
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

        protected override void SerializeMiddle(BinaryWriter writer)
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
            writer.Write(Width);
            writer.Write(Height);

            for (int i = 0; i < depthData.Length; i++)
                writer.Write(depthData[i]);
        }
    }
}
