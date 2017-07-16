﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Kinect;

namespace KSIM.Readers
{
    public sealed class HeadDepthReader : DepthReader
    {
        public override Frame Read(MultiSourceFrame f)
        {
            // Note that we do not dispose the acquired frame
            // that responsibility is delegated to newly created frame
            var originalDepthFrame = f.DepthFrameReference.AcquireFrame();
            var cbf = (ClosestBodyFrame)FrameType.ClosestBody.GetReader().Read(f);

            if (cbf != null && originalDepthFrame != null)
            {
                return new HeadDepthFrame(originalDepthFrame, cbf);
            }
            else
            {
                if (cbf != null)
                    cbf.Dispose();
                if (originalDepthFrame != null)
                    originalDepthFrame.Dispose();
            }
            return null;
        }
    }

    public sealed class HeadDepthFrame : SegmentedDepthFrame
    {
        public HeadDepthFrame(Microsoft.Kinect.DepthFrame df, ClosestBodyFrame cbf) : base(df, cbf)
        {
            Type = FrameType.HeadDepth;
        }

        protected override void SetCenter()
        {
            var pos = UnderlyingClosestBodyFrame.Joints[JointType.Head].Position;
            DepthSpacePoint p = UnderlyingDepthFrame.DepthFrameSource.KinectSensor.CoordinateMapper.MapCameraPointToDepthSpace(pos);
            posX = p.X;
            posY = p.Y;
        }

        private bool disposed = false;

        protected override void Dispose(bool disposing)
        {
            disposed = true;
            base.Dispose(disposing);
        }
    }
}
