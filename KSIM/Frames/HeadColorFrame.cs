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
        private Microsoft.Kinect.KinectSensor sensor;
        public HeadColorFrame(Microsoft.Kinect.ColorFrame cf, Microsoft.Kinect.DepthFrame df, Microsoft.Kinect.KinectSensor sensor, ClosestBodyFrame cbf) : base(cf, df, cbf)
        {
            Type = FrameType.HeadColor;
            this.sensor = sensor;
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
        protected override void SubtractBackground()
        {
            int depthFrameWidth = UnderlyingDepthFrame.DepthFrameSource.FrameDescription.Width;
            int depthFrameHeight = UnderlyingDepthFrame.DepthFrameSource.FrameDescription.Height;
            ushort z = 0;
            ushort[] depthData = new ushort[depthFrameWidth * depthFrameHeight];
            ColorSpacePoint[] colorPointData = new ColorSpacePoint[depthFrameWidth * depthFrameHeight];
            var pos = UnderlyingClosestBodyFrame.Joints[JointType.Head].Position;

            DepthSpacePoint p = UnderlyingDepthFrame.DepthFrameSource.KinectSensor.CoordinateMapper.MapCameraPointToDepthSpace(pos);

            // Copy depth data to memory reserved earlier
            UnderlyingDepthFrame.CopyFrameDataToArray(depthData);
            sensor.CoordinateMapper.MapDepthFrameToColorSpace(depthData, colorPointData);

            z = depthData[(int)p.Y * depthFrameWidth + (int)p.X];

            for( int i = 0; i < depthFrameHeight; i++ )
            {
                for( int j = 0; j < depthFrameWidth; j++)
                {
                    if ( depthData[i * depthFrameWidth + j] < z - 10 || depthData[i * depthFrameWidth + j] > z + 10)
                    {
                        colorDataBitmap.SetPixel((int) colorPointData[i * depthFrameWidth + j].X, (int) colorPointData[i * depthFrameWidth + j].Y, System.Drawing.Color.Black);
                    }
                }
            }
        }
    }
}
