
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Kinect;
using System.Diagnostics;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Media;


namespace KSIM.Frames
{
    public abstract class SegmentedColorFrame : ColorFrame
    {
        private Rectangle crop; // Determines area to crop
        private Bitmap croppedColorDataBitMap = null; // Creates a bitmap of the cropped image

        private ClosestBodyFrame underlyingClosestBodyFrame = null;

        protected ClosestBodyFrame UnderlyingClosestBodyFrame
        {
            get { return underlyingClosestBodyFrame; }
        }

        public int SegmentedWidth
        {
            get { return xEnd - xStart; }
        }

        public int SegmentedHeight
        {
            get { return yEnd - yStart; }
        }

        public int SegmentedStride
        {
            get { return (SegmentedWidth * PixelFormats.Bgra32.BitsPerPixel + 7) / 8; }
        }

        private bool segmented = false;
        public bool Segmented
        {
            get { return segmented; }
        }

        public SegmentedColorFrame(Microsoft.Kinect.ColorFrame cf, ClosestBodyFrame cbf) : base(cf)
        {
            underlyingClosestBodyFrame = cbf;

            SetCenter();

            // If unable to segment, then the reader should return a null frame
            segmented = Segment();
            // No need to threshold because there is no depth data
        }

        protected abstract void SetCenter();


        private bool isColorInvalid = false;
        private int fallbackSize = 168;
        // Because the training net requires a value between 0 and 255
        // One could make it the maximum possible for a depth frame that could be normalized by the client
        // or alternatively, output the normalized one beforehand as is done here.
        private ushort fallbackValue = 255;

        private const int CubeSize = 200,
                          CubeSizeZ = 300;

        private const float fx = 288.03f,
                            fy = 287.07f;

        // Correspond to the boundaries of Virtual Frame computed by Segment()
        protected int xStart = 0, xEnd = 0, yStart = 0, yEnd = 0;

        // Correspond to the point w.r.t. which the virtual frame is computed
        // By default, an invalid value so that Segment() returns false
        protected float posX = -1.0f, posY = -1.0f;


        protected virtual bool Segment()
        {
            if (posX < 0 || posY < 0)
            {
                xStart = (int)((Width / 2) - (CubeSize / 2));
                xEnd = (int)((Width / 2) + (CubeSize / 2));

                yStart = (int)((Height / 2) - (CubeSize / 2));
                yEnd = (int)((Height / 2) + (CubeSize / 2));
            }
            else
            {
                xStart = (int)(posX - (CubeSize / 2));
                xEnd = (int)(posX + (CubeSize / 2));

                yStart = (int)(posY - (CubeSize / 2));
                yEnd = (int)(posY + (CubeSize / 2));
            }
            SetRectangle();
            return true;
        }


        protected override void SerializeMiddle(BinaryWriter writer)
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
            Debug.Write(String.Format("{0} x {1}\n", SegmentedWidth, SegmentedHeight));

            writer.Write(0);
            writer.Write(Segmented ? SegmentedWidth : 0);
            writer.Write(Segmented ? SegmentedHeight : 0);

            if (Segmented)
            {
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

                // The below logic relies on the fact that we have sane values for posX and posY
                // If those are outside the actual frame, this will probably not work properly
                int xStartInFrame = xStart >= 0 ? xStart : 0,
                    xEndInFrame = xEnd <= Width ? xEnd : Width,

                    yStartInFrame = yStart >= 0 ? yStart : 0,
                    yEndInFrame = yEnd <= Height ? yEnd : Height;


                Debug.Write(String.Format("Virtual frame: ({0}, {1}) and ({2}, {3})\n", xStart, yStart, xEnd, yEnd));

                // Zero rows to account for some part of the virtual frame being above the top boundary of actual frame
                for (int i = 0; i < prepend_rows; i++)
                    for (int j = 0; j < SegmentedWidth; j++)
                        writer.Write((ushort)0);

                int xStartInBuffer = IndexIntoColorData(xStartInFrame, yStartInFrame),
                    xEndInBuffer = IndexIntoColorData(xEndInFrame, yStartInFrame);

                Debug.Assert(SegmentedHeight == (prepend_rows + (yEndInFrame - yStartInFrame) + append_rows), String.Format("Mismatch in segmented height: {0} = {1} + ({2} - {3}) + {4}\n", SegmentedHeight, prepend_rows, yEndInFrame, yStartInFrame, append_rows));

                Debug.Assert(SegmentedWidth == (prepend_zeros + (xEndInBuffer - xStartInBuffer) + append_zeros), String.Format("Mismatch in segmented width: {0} = {1} + ({2} - {3}) + {4}\n", SegmentedWidth, prepend_zeros, xEndInBuffer, xStartInBuffer, append_zeros));

                croppedColorDataBitMap = colorDataBitmap.Clone(crop, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                croppedColorDataBitMap.Save(compressedColorDataStream, ImageFormat.Jpeg);
                //croppedColorDataBitMap.Save(compressedColorDataStream, ImageFormat.Bmp);

                writer.Write(compressedColorDataStream.ToArray().Length);
                writer.Write(compressedColorDataStream.ToArray());

                // Zero rows to account for some part of the virtual frame being below the bottom boundary of actual frame
                for (int i = 0; i < append_rows; i++)
                    for (int j = 0; j < SegmentedWidth; j++)
                        writer.Write((ushort)0);
            }
        }

        private void SetRectangle()
        {
            if(xStart < 0)
            {
                xStart = 0;
                xEnd = CubeSize;
            }
            if(xEnd >= Width)
            {
                xEnd = Width - 1;
                xStart = Width - CubeSize - 1;
            }
            if (yStart < 0)
            {
                yStart = 0;
                yEnd = CubeSize;
            }
            if (yEnd >= Height)
            {
                yEnd = Height - 1;
                yStart = Height - CubeSize - 1;
            }
            crop = new Rectangle(xStart, yStart, SegmentedWidth, SegmentedHeight);
            //crop = new Rectangle(0, 0, Width, Height);
        }
    }
}

