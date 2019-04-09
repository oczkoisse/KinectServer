using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Kinect;

namespace KSIM.Readers
{
    public sealed class CroppedHeadDepthReader : DepthReader
    {
        public override Frame Read(MultiSourceFrame f)
        {
            var originalDepthFrame = f.DepthFrameReference.AquireFrame();
            var cbf = (ClosestBodyFrame)FrameType.ClosestBody.GetReader().Read(f);

            if(cbf != null && originalDepthFrame != null)
            {
                // returns the right hand depth frame
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
}
public sealed class CroppedHeadDepthFrame : SegmentedDepthFrame
{
    public CroppedHeadDepthFrame(Microsoft.Kinect.DepthFrame df, ClosestBodyFrame cbf) : base(df, cbf)
    {
        Type = FrameType.HeadDepth
    }

    protected override void SetCenter()
    {
        if (UnderlyingClosestBodyFrame.Engaged)
        {
            var pos = UnderlyingClosestBodyFrame.Joints[JointType.Head].Position;
            DepthSpacePoint p = UnderlyingDepthFrame.DepthFrameSource.KinectSensor.CoordinateMapper.MapCameraPointToDepthSpace(pos);
        }
    }

}