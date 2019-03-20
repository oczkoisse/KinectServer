using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Kinect;

namespace KSIM.Frames
{
    public sealed class LHDepthFrame : SegmentedDepthFrame
    {
        public LHDepthFrame(Microsoft.Kinect.DepthFrame df, ClosestBodyFrame cbf) : base(df, cbf)
        {
            Type = FrameType.LHDepth;
        }

        protected override void SetCenter()
        {
            if (UnderlyingClosestBodyFrame.Engaged)
            {
                var pos = UnderlyingClosestBodyFrame.Joints[JointType.HandLeft].Position;
                DepthSpacePoint p = UnderlyingDepthFrame.DepthFrameSource.KinectSensor.CoordinateMapper.MapCameraPointToDepthSpace(pos);
                posX = p.X;
                posY = p.Y;
            }
        }
    }
}
