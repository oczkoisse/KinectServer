using System;

namespace KSIM.Kinect
{
    public abstract class KinectEventArgs : EventArgs
    {
        public long Timestamp
        {
            get;
        }

        public KinectEventArgs(long timestamp)
        {
            Timestamp = timestamp;
        }
    }

    public class MultiSourceFrameArrivedEventArgs : KinectEventArgs
    {
        public Microsoft.Kinect.ColorFrame ColorFrame
        {
            get;
        }

        public Microsoft.Kinect.DepthFrame DepthFrame
        {
            get;
        }

        public Microsoft.Kinect.BodyFrame BodyFrame
        {
            get;
        }

        public Microsoft.Kinect.BodyIndexFrame BodyIndexFrame
        {
            get;
        }

        public Microsoft.Kinect.InfraredFrame InfraredFrame
        {
            get;
        }

        public MultiSourceFrameArrivedEventArgs(long timestamp, Microsoft.Kinect.ColorFrame colorFrame,
            Microsoft.Kinect.DepthFrame depthFrame, Microsoft.Kinect.BodyFrame bodyFrame,
            Microsoft.Kinect.BodyIndexFrame bodyIndexFrame, Microsoft.Kinect.InfraredFrame infraredFrame) : base(timestamp)
        {
            ColorFrame = colorFrame;
            DepthFrame = depthFrame;
            BodyFrame = bodyFrame;
            BodyIndexFrame = bodyIndexFrame;
            InfraredFrame = infraredFrame;
        }
    }

    public class AudioBeamFrameArrivedEventArgs : KinectEventArgs
    {
        public Microsoft.Kinect.AudioBeamFrameList AudioBeamFrameList
        {
            get;
        }

        public AudioBeamFrameArrivedEventArgs(long timestamp, Microsoft.Kinect.AudioBeamFrameList audioBeamFrameList) : base(timestamp)
        {
            AudioBeamFrameList = audioBeamFrameList;
        }
    }

    public class FaceFrameArrivedEventArgs : KinectEventArgs
    {
        public Microsoft.Kinect.Face.FaceFrameResult FaceFrameResult
        {
            get;
        }

        public FaceFrameArrivedEventArgs(long timestamp, Microsoft.Kinect.Face.FaceFrameResult faceFrameResult) : base(timestamp)
        {
            FaceFrameResult = faceFrameResult;
        }
    }
}
