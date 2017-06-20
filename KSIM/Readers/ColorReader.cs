using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Kinect;

/**
namespace KSIM.Readers
{
    public sealed class ColorReader : Reader
    { 
        public override Frame read(MultiSourceFrame f)
        {
            KSIM.Readers.ColorReader.ColorFrame colorFrame = null;
            using (var originalFrame = f.ColorFrameReference.AcquireFrame())
            {
                if (originalFrame == null)
                    throw new NullReferenceException("Can't retrieve Color frame");
                colorFrame = new ColorFrame(originalFrame);
            }
            return colorFrame; 
        }

        public sealed class ColorFrame : Frame
        {
            private byte[] colorData = null;
            private Bitmap bitmap = null;
            private IntPtr bitmapPtr = IntPtr.Zero;

            public ColorFrame(Microsoft.Kinect.ColorFrame cf)
            {
                Width = cf.FrameDescription.Width;
                Height = cf.FrameDescription.Height;
                
                colorData = new byte[Width * Height * cf.FrameDescription.BytesPerPixel];

                if (cf.RawColorImageFormat == ColorImageFormat.Bgra)
                {
                    cf.CopyRawFrameDataToArray(colorData);
                }
                else
                {
                    cf.CopyConvertedFrameDataToArray(colorData, ColorImageFormat.Bgra);
                }

                bitmap = new Bitmap(Width, Height, 0, PixelFormat.Format32bppRgb, colorData);
            }

            

            public override void Serialize(Stream stream)
            {
                throw new NotImplementedException();
            }

            protected override void Dispose(bool disposing)
            {
                if (!disposed)
                {
                    if (disposing)
                    {
                        // TODO: dispose managed state (managed objects).
                    }
                    if (underlyingBodyFrame != null)
                        underlyingBodyFrame.Dispose();
                    disposed = true;
                }
            }
        }
    }


}
 */