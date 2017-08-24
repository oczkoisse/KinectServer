using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Kinect;

namespace KSIM.Readers
{
    public sealed class RHDepthReader : DepthReader
    {
        public override Frame Read(MultiSourceFrame f)
        {
            // Note that we do not dispose the acquired frame
            // that responsibility is delegated to newly created frame
            var originalDepthFrame = f.DepthFrameReference.AcquireFrame();
            var cbf = (ClosestBodyFrame)FrameType.ClosestBody.GetReader().Read(f);

            if (cbf != null && originalDepthFrame != null)
            {
                return new RHDepthFrame(originalDepthFrame, cbf);
            }
            else
            {
                if (cbf != null)
                    cbf.Dispose();
                if (originalDepthFrame != null)
                    originalDepthFrame.Dispose();
                return null;
            }
        }
    }

    public sealed class RHDepthFrame : SegmentedDepthFrame
    {
        public RHDepthFrame(Microsoft.Kinect.DepthFrame df, ClosestBodyFrame cbf) : base(df, cbf)
        {
            Type = FrameType.RHDepth;
        }

        protected override void SetCenter()
        {
            if (UnderlyingClosestBodyFrame.Engaged)
            {
                var pos = UnderlyingClosestBodyFrame.Joints[JointType.HandRight].Position;
                DepthSpacePoint p = UnderlyingDepthFrame.DepthFrameSource.KinectSensor.CoordinateMapper.MapCameraPointToDepthSpace(pos);
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
