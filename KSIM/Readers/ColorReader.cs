using System;
using System.IO;
using Microsoft.Kinect;
using System.Windows.Media;

namespace KSIM.Readers
{
    public sealed class ColorReader : Reader
    { 
        public override Frame Read(MultiSourceFrame f)
        {
            // Note that we do not dispose the acquired frame
            // that responsibility is delegated to newly created frame
            var originalFrame = f.ColorFrameReference.AcquireFrame();
            
            if (originalFrame == null)
                return null;
            else
                return new ColorFrame(originalFrame); 
        }

        public sealed class ColorFrame : Frame
        {
            private bool disposed = false;

            private Microsoft.Kinect.ColorFrame underlyingColorFrame = null;

            protected Microsoft.Kinect.ColorFrame UnderlyingColorFrame
            {
                get { return underlyingColorFrame; }
            }

            private byte[] colorData = null;
            private int stride = 0;
            
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
                        underlyingColorFrame.CopyConvertedFrameDataToArray(colorData, ColorImageFormat.Bgra);
                    }
                }
            }


            protected override void SerializeMiddle(BinaryWriter writer)
            {
                writer.Write(stride);
                writer.Write(Width);
                writer.Write(Height);

                writer.Write(colorData);
            }

            protected override void Dispose(bool disposing)
            {
                if (!disposed)
                {
                    if (disposing)
                    {
                        // TODO: dispose managed state (managed objects).
                        if (underlyingColorFrame != null)
                            underlyingColorFrame.Dispose();
                    }
                    
                    disposed = true;
                }
            }
        }
    }
}

