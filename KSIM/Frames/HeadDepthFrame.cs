using Microsoft.Kinect;

namespace KSIM.Frames
{
    public sealed class HeadDepthFrame : SegmentedDepthFrame
    {
        public HeadDepthFrame(Microsoft.Kinect.DepthFrame df, ClosestBodyFrame cbf) : base(df, cbf)
        {
            Type = FrameType.HeadDepth;
        }

        protected override void SetCenter()
        {
            if (UnderlyingClosestBodyFrame.Engaged)
            {
                var pos = UnderlyingClosestBodyFrame.Joints[JointType.Head].Position;
                DepthSpacePoint p = UnderlyingDepthFrame.DepthFrameSource.KinectSensor.CoordinateMapper.MapCameraPointToDepthSpace(pos);
                posX = p.X;
                posY = p.Y;
            }
        }
    }
}
