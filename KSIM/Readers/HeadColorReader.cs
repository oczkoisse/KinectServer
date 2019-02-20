using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Kinect;

namespace KSIM.Readers
{
    public sealed class HeadColorReader : ColorReader
    {
        public override Frame Read(MultiSourceFrame f)
        {
            // Note that we do not dispose the acquired frame
            // that responsibility is delegated to newly created frame
            var originalColorFrame = f.ColorFrameReference.AcquireFrame();
            var cbf = (ClosestBodyFrame)FrameType.ClosestBody.GetReader().Read(f);

            if (cbf != null && originalColorFrame != null)
            {
                return new HeadColorFrame(originalColorFrame, cbf);
            }
            else
            {
                if (cbf != null)
                    cbf.Dispose();
                if (originalColorFrame != null)
                    originalColorFrame.Dispose();
                return null;
            }
        }
    }

    public sealed class HeadColorFrame : SegmentedColorFrame
    {
        public HeadColorFrame(Microsoft.Kinect.ColorFrame cf, ClosestBodyFrame cbf) : base(cf, cbf)
        {
            Type = FrameType.HeadDepth;
        }

        protected override void SetCenter()
        {
            if (UnderlyingClosestBodyFrame.Engaged)
            {
                var pos = UnderlyingClosestBodyFrame.Joints[JointType.Head].Position;
                ColorSpacePoint p = UnderlyingColorFrame.ColorFrameSource.KinectSensor.CoordinateMapper.MapCameraPointToColorSpace(pos);
                posX = p.X;
                posY = p.Y;
            }
        }

        private bool disposed = false;

        protected override void Dispose(bool disposing)
        {
            disposed = true;
            base.Dispose(disposing);
        }
    }
}
