using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Kinect;

namespace KSIM.Readers
{
    public sealed class LHDepthReader : DepthReader
    {
        public override Frame read(MultiSourceFrame f)
        {
            // Note that we do not dispose the acquired frame
            // that responsibility is delegated to newly created frame
            var originalDepthFrame = f.DepthFrameReference.AcquireFrame();
            var originalBodyFrame = f.BodyFrameReference.AcquireFrame();

            if (originalBodyFrame == null || originalDepthFrame == null)
                return null;
            else
            {
                var cbr = new ClosestBodyReader();
                ClosestBodyFrame cbf = (ClosestBodyFrame)cbr.read(f);
                return new LHDepthFrame(originalDepthFrame, cbf);
            }
        }
    }

    public sealed class LHDepthFrame : SegmentedDepthFrame
    {
        public LHDepthFrame(Microsoft.Kinect.DepthFrame df, ClosestBodyFrame cbf) : base(df, cbf)
        {
            Type = FrameType.LHDepth;
        }

        protected override void SetCenter()
        {
            var pos = UnderlyingClosestBodyFrame.Joints[JointType.HandLeft].Position;
            DepthSpacePoint p = UnderlyingDepthFrame.DepthFrameSource.KinectSensor.CoordinateMapper.MapCameraPointToDepthSpace(pos);
            posX = p.X;
            posY = p.Y;
        }
    }
}
