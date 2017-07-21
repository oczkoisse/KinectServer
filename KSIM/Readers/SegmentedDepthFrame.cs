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
    public abstract class SegmentedDepthFrame : KSIM.Readers.DepthFrame
    {
        private bool disposed = false;

        private ClosestBodyFrame underlyingClosestBodyFrame = null;

        protected ClosestBodyFrame UnderlyingClosestBodyFrame
        {
            get { return underlyingClosestBodyFrame;  }
        }

        public int SegmentedWidth
        {
            get { return xEnd - xStart; }
        }

        public int SegmentedHeight
        {
            get { return yEnd - yStart; }
        }

        private bool segmented = false;
        public bool Segmented
        {
            get { return segmented; }
        }

        public SegmentedDepthFrame(Microsoft.Kinect.DepthFrame df, ClosestBodyFrame cbf) : base(df)
        {
            underlyingClosestBodyFrame = cbf;

            SetCenter();

            // May want to throw an invalid state exception if cropping fails
            segmented = Segment();

            if (segmented)
                Threshold();
        }

        protected abstract void SetCenter();
        

        private bool isDepthInvalid = false;
        private int fallbackSize = 168;
        // Because the training net requires a value between 0 and 255
        // One could make it the maximum possible for a depth frame that could be normalized by the client
        // or alternatively, output the normalized one beforehand as is done here.
        private ushort fallbackValue = 255;

        private const int CubeSize = 396,
                          CubeSizeZ = 300;

        private const float fx = 288.03f,
                            fy = 287.07f;
        
        // Correspond to the boundaries of Virtual Frame computed by Segment()
        protected int xStart = 0, xEnd = 0, yStart = 0, yEnd = 0;

        // Correspond to the point w.r.t. which the virtual frame is computed
        protected float posX = 0.0f, posY = 0.0f;

        private ushort posZ = 0;

        protected virtual bool Segment()
        {
            int index = IndexIntoDepthData(posX, posY);
            if (index == -1)
                return false;

            posZ = DepthData[index];

            if (posZ == 0)
                isDepthInvalid = true;

            if (isDepthInvalid)
            {
                // The boundaries below use the fact that
                // if depth is invalid (0), then the depth frame is
                // all fallbackValue valued with size fallbackSize x fallbackSize
                xStart = 0;
                xEnd = fallbackSize;
                yStart = 0;
                yEnd = fallbackSize;
                // Overwrite posX and posY because they are not in the original frame now
                posX = fallbackSize / 2.0f;
                posY = fallbackSize / 2.0f;
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

            for (int xStartInBuffer = IndexIntoDepthData(xStartInFrame, yStartInFrame),
                    xEndInBuffer = IndexIntoDepthData(xEndInFrame, yStartInFrame);
                    xEndInBuffer <= depthData.Length;
                    xStartInBuffer += Width, xEndInBuffer += Width)
            {
                for (int i = xStartInBuffer; i < xEndInBuffer; i++)
                {
                    if (isDepthInvalid)
                        depthData[i] = fallbackValue;
                    else if (depthData[i] == 0)
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
            // Load Size (4 bytes, signed) | Timestamp (8 bytes, signed) | Frame Type (4 bytes, bitset) | Width (4 bytes, signed) | Height (4 bytes, signed) | Depth Data (2 bytes * Width * Height, unsigned)
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
                writer.Write(1 << (int)Type);

                Debug.Write(String.Format("{0} x {1}\n", SegmentedWidth, SegmentedHeight));
                
                writer.Write(SegmentedWidth);
                writer.Write(SegmentedHeight);

                // Position of hand in the segmented frame
                writer.Write(posX - xStart);
                writer.Write(posY - yStart);

                // Write the segmented data using the boundaries determined by Segment()
                int prepend_zeros = 0, append_zeros = 0, prepend_rows = 0, append_rows = 0;

                // For the left boundary of the actual frame
                if (xEnd < 0)
                    // The virtual frame is completely to the left of the boundary
                    prepend_zeros = xEnd - xStart;
                else if (xStart < 0)
                    // At least some part of virtual frame is to the left of the boundary
                    prepend_zeros = -xStart;
                else
                    // No part of virtual frame is to the left of the boundary
                    prepend_zeros = 0;

                // For the right boundary of the actual frame
                if (xStart >= Width)
                    // The virtual frame is completely to the right of the boundary
                    append_zeros = xEnd - xStart;
                else if (xEnd > Width)
                    // At least some part of virtual frame is to the right of the boundary
                    append_zeros = xEnd - Width;
                else
                    // No part of virtual frame is to the right of the boundary 
                    append_zeros = 0;

                // For the top boundary of the actual frame
                if (yEnd < 0)
                    // The virtual frame is completely above the boundary
                    prepend_rows = yEnd - yStart;
                else if (yStart < 0)
                    // At least some part of virtual frame is above the boundary
                    prepend_rows = -yStart;
                else
                    // No part of virtual frame is above the boundary
                    prepend_rows = 0;

                // For the bottom boundary of the actual frame
                if (yStart >= Height)
                    // The virtual frame is completely below the boundary
                    append_rows = yEnd - yStart;
                else if (yEnd > Height)
                    // At least some part of virtual frame is below the boundary
                    append_rows = yEnd - Height;
                else
                    // No part of virtual frame is below the boundary
                    append_rows = 0;

                int xStartInFrame = xStart >= 0 ? xStart : 0,
                    xEndInFrame = xEnd <= Width ? xEnd : Width,

                    yStartInFrame = yStart >= 0 ? yStart : 0,
                    yEndInFrame = yEnd <= Height ? yEnd : Height;


                Debug.Write(String.Format("Virtual frame: ({0}, {1}) and ({2}, {3})\n", xStart, yStart, xEnd, yEnd));
                
                // Zero rows to account for some part of the virtual frame being above the top boundary of actual frame
                for (int i = 0; i < prepend_rows; i++)
                    for (int j = 0; j < SegmentedWidth; j++)
                        writer.Write((ushort)0);

                int xStartInBuffer = IndexIntoDepthData(xStartInFrame, yStartInFrame),
                    xEndInBuffer = IndexIntoDepthData(xEndInFrame, yStartInFrame);

                Debug.Assert(SegmentedHeight == (prepend_rows + (yEndInFrame - yStartInFrame) + append_rows), String.Format("Mismatch in segmented height: {0} = {1} + ({2} - {3}) + {4}\n", SegmentedHeight, prepend_rows, yEndInFrame, yStartInFrame, append_rows));

                Debug.Assert(SegmentedWidth == (prepend_zeros + (xEndInBuffer - xStartInBuffer) + append_zeros), String.Format("Mismatch in segmented width: {0} = {1} + ({2} - {3}) + {4}\n", SegmentedWidth, prepend_zeros, xEndInBuffer, xStartInBuffer, append_zeros));

                for (int i = 0; i < yEndInFrame - yStartInFrame; i++)
                {
                    // Zero columns to accound for some part of the virtual frame being to the left of actual frame
                    for (int j = 0; j < prepend_zeros; j++)
                        writer.Write((ushort)0);
                    
                    for (int j = xStartInBuffer; j < xEndInBuffer; j++)
                        writer.Write(depthData[j]);
                    
                    // Go to next row in actual frame
                    xStartInBuffer += Width;
                    xEndInBuffer += Width;

                    // Zero columns to accound for some part of the virtual frame being to the right of actual frame
                    for (int j = 0; j < append_zeros; j++)
                        writer.Write((ushort)0);
                }

                // Zero rows to account for some part of the virtual frame being below the bottom boundary of actual frame
                for (int i = 0; i < append_rows; i++)
                    for (int j = 0; j < SegmentedWidth; j++)
                        writer.Write((ushort)0);
                
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
                    if (underlyingClosestBodyFrame != null)
                        underlyingClosestBodyFrame.Dispose();
                }
            }
            disposed = true;
            base.Dispose(disposing);
        }
    }
}

