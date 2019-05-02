using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Kinect;

namespace KSIM.Frames
{
    public sealed class HeadColorFrame : SegmentedColorFrame
    {
        public HeadColorFrame(Microsoft.Kinect.ColorFrame cf, ClosestBodyFrame cbf) : base(cf, cbf)
        {
            Type = FrameType.HeadColor;
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
    }
}
