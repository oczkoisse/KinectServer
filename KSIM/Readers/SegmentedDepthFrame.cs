using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Kinect;
using System.Diagnostics;
using System.IO;

namespace KSIM.Readers
{
    public abstract class SegmentedDepthFrame : KSIM.Readers.DepthReader.DepthFrame
    {
        private ClosestBodyReader.ClosestBodyFrame underlyingClosestBodyFrame = null;

        protected ClosestBodyReader.ClosestBodyFrame UnderlyingClosestBodyFrame
        {
            get { return underlyingClosestBodyFrame;  }
        }

        private int segmentedWidth = 0, segmentedHeight = 0;
        public int SegmentedWidth
        {
            get { return segmentedWidth; }
        }

        public int SegmentedHeight
        {
            get { return segmentedHeight; }
        }

        public SegmentedDepthFrame(Microsoft.Kinect.DepthFrame df, ClosestBodyReader.ClosestBodyFrame cbf) : base(df)
        {
            underlyingClosestBodyFrame = cbf;

            SetCenter();

            bool segmented = Segment();

            if (segmented)
            {
                segmentedWidth = xEnd - xStart;
                segmentedHeight = yEnd - yStart;
            }
        }

        protected abstract void SetCenter();
        

        private bool isDepthInvalid = false;
        private int fallbackSize = 168;
        private ushort fallbackValue = 255;

        private const int CubeSize = 396,
                          CubeSizeZ = 300;

        private const float fx = 288.03f,
                            fy = 287.07f;

        // Should be set by Segment()
        protected int xStart = 0, xEnd = 0, yStart = 0, yEnd = 0;

        // Should be set by SetCenter()
        protected float posX = 0.0f, posY = 0.0f;

        private ushort posZ = 0;

        protected virtual bool Segment()
        {
            int index = IndexIntoDepthData(posX, posY);
            if (index < 0 || index >= depthData.Length)
                return false;

            posZ = DepthData[index];

            if (posZ == 0)
                isDepthInvalid = true;

            if (isDepthInvalid)
            {
                // The boundaries below rely on the fact that
                // is depth is invalid (0), then the depth frame is
                // all fallbackValue valued with size fallbackSize x fallbackSize
                xStart = 0;
                xEnd = fallbackSize;
                yStart = 0;
                yEnd = fallbackSize;
            }
            else
            {
                xStart = (int)((((posX * posZ / fx) - (CubeSize / 2.0)) / posZ) * fx);
                xEnd = (int)((((posX * posZ / fx) + (CubeSize / 2.0)) / posZ) * fx);

                yStart = (int)((((posY * posZ / fy) - (CubeSize / 2.0)) / posZ) * fy);
                yEnd = (int)((((posY * posZ / fy) + (CubeSize / 2.0)) / posZ) * fy);

                Debug.Assert(xEnd > xStart);
                Debug.Assert(yEnd > yStart);
            }

            return true;
        }

        public virtual void Threshold()
        {
            // Threshold bounds
            double zStart = posZ - CubeSizeZ / 2.0,
                   zEnd = posZ + CubeSizeZ / 2.0;

            int xStartInFrame = xStart >= 0 ? xStart : 0,
                xEndInFrame = xEnd <= Width ? xEnd : Width,

                yStartInFrame = yStart >= 0 ? yStart : 0,
                yEndInFrame = yEnd <= Height ? yEnd : Height;

            for (int start = IndexIntoDepthData(xStartInFrame, yStartInFrame),
                    end = IndexIntoDepthData(xEndInFrame, yStartInFrame);
                    end <= depthData.Length;
                    start += Width, end += Width)
            {
                for (int i = start; i < end; i++)
                {
                    if (depthData[i] == 0)
                        depthData[i] = (ushort)zEnd;
                    else if (depthData[i] > zEnd)
                        depthData[i] = (ushort)zEnd;
                    else if (depthData[i] < zStart)
                        depthData[i] = (ushort)zStart;
                }
            }
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

                writer.Write(SegmentedWidth);
                writer.Write(SegmentedHeight);

                // Position of hand in the segmented frame
                writer.Write(posX - xStart);
                writer.Write(posY - yStart);

                // Write the segmented data using the boundaries determined by Segment()
                int prepend_zeros = 0, append_zeros = 0, prepend_rows = 0, append_rows = 0;
                if (xEnd < 0)
                    prepend_zeros = xEnd - xStart;
                else if (xStart < 0)
                    prepend_zeros = -xStart;
                else
                    prepend_zeros = 0;

                if (xStart >= Width)
                    append_zeros = xEnd - xStart;
                else if (xEnd > Width)
                    append_zeros = xEnd - Width;
                else
                    append_zeros = 0;

                if (yEnd < 0)
                    prepend_rows = yEnd - yStart;
                else if (yStart < 0)
                    prepend_rows = -yStart;
                else
                    prepend_rows = 0;

                if (yStart >= Height)
                    append_rows = yEnd - yStart;
                else if (yEnd > Width)
                    append_rows = yEnd - Height;
                else
                    append_rows = 0;

                int xStartInFrame = xStart >= 0 ? xStart : 0,
                xEndInFrame = xEnd <= Width ? xEnd : Width;

                const ushort zero = 0;

                //Write prepended zero rows of width xEnd - xStart
                for (int i = 0; i < prepend_rows; i++)
                    for (int j = 0; j < SegmentedWidth; j++)
                        writer.Write(zero);

                for (int i = 0; i < yEnd - yStart; i++)
                {
                    for (int j = 0; j < prepend_zeros; j++)
                        writer.Write(zero);
                    for (int j = xStartInFrame; j < xEndInFrame; j++)
                        writer.Write(depthData[j]);
                    for (int j = 0; j < append_zeros; j++)
                        writer.Write(zero);
                }

                //Write appended zero rows of width xEnd - xStart
                for (int i = 0; i < append_rows; i++)
                    for (int j = 0; j < SegmentedWidth; j++)
                        writer.Write(zero);

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
                base.Dispose();
                underlyingClosestBodyFrame.Dispose();
                disposed = true;
            }
        }
    }
}

