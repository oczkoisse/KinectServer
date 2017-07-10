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
        public override Frame read(MultiSourceFrame f)
        {
            // Note that we do not dispose the acquired frame
            // that responsibility is delegated to newly created frame
            var originalDepthFrame = f.DepthFrameReference.AcquireFrame();
            var originalBodyFrame = f.BodyFrameReference.AcquireFrame();

            if (originalBodyFrame == null || originalDepthFrame == null)
                throw new NullReferenceException("Null frame returned by Kinect");

            var cbr = new ClosestBodyReader();

            ClosestBodyFrame cbf = (ClosestBodyFrame)cbr.read(f);
            return new RHDepthFrame(originalDepthFrame, cbf);
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
            var pos = UnderlyingClosestBodyFrame.Joints[JointType.HandRight].Position;
            DepthSpacePoint p = UnderlyingDepthFrame.DepthFrameSource.KinectSensor.CoordinateMapper.MapCameraPointToDepthSpace(pos);
            posX = p.X;
            posY = p.Y;
        }
    }
}
