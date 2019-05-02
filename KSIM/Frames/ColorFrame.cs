
using System.IO;
using Microsoft.Kinect;
using System.Windows.Media;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Imaging;

namespace KSIM.Frames
{
    public class ColorFrame : Frame
    {

        private Microsoft.Kinect.ColorFrame underlyingColorFrame = null;

        protected Microsoft.Kinect.ColorFrame UnderlyingColorFrame
        {
           get { return underlyingColorFrame; }
        }

        protected Bitmap colorDataBitmap = null;
        protected byte[] colorData = null; // TODO: Ask if this is acceptable or try to add read only functionality
        protected int stride = 0;
        protected  MemoryStream compressedColorDataStream = new MemoryStream();
        protected int num_bytes = 0;

        public ColorFrame(Microsoft.Kinect.ColorFrame cf)
        {
            Type = FrameType.Color;

            underlyingColorFrame = cf;

            FrameDescription cfd = underlyingColorFrame.CreateFrameDescription(ColorImageFormat.Bgra);
            Width = cfd.Width;
            Height = cfd.Height;

            // Stride is the number of bytes allocated for one scanline of the bitmap
            // Fact: Scanlines must be aligned on 32-bit boundaries
            // So, each scanline may have padded bytes at the end to ensure this
            // so stride is: ((w * bitsPerPixel + (padBits-1)) / padBits) * padToNBytes
            // where padBits = 8 * padToNBytes
            // See https://msdn.microsoft.com/en-us/library/windows/desktop/aa473780(v=vs.85).aspx
            stride = (Width * PixelFormats.Bgra32.BitsPerPixel + 7) / 8;

            if (colorData == null)
            {
                colorData = new byte[stride * Height];

                using (KinectBuffer colorBuffer = cf.LockRawImageBuffer())
                {
                    // TODO: crop and convert to jpeg. 
                    underlyingColorFrame.CopyConvertedFrameDataToArray(colorData, ColorImageFormat.Bgra);
                }

                colorDataBitmap = new Bitmap(Width, Height, stride,
                         System.Drawing.Imaging.PixelFormat.Format32bppArgb,
                         Marshal.UnsafeAddrOfPinnedArrayElement(colorData, 0));

            }
        }

        protected int IndexIntoColorData(float x, float y)
        {
            return IndexIntoColorData((int)x, (int)y);
        }

        protected int IndexIntoColorData(int x, int y)
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

        protected override void SerializeMiddle(BinaryWriter writer)
        {
            writer.Write(stride);
            writer.Write(Width);
            writer.Write(Height);

            colorDataBitmap.Save(compressedColorDataStream, ImageFormat.Jpeg);
            byte[] colorData = compressedColorDataStream.ToArray();

            num_bytes = colorData.Length;
            writer.Write(num_bytes);

            // Compresses the colorData array into jpeg format in the compressedColorDataStream
            
            writer.Write(colorData);
        }
    }
}

