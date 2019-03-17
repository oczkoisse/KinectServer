using System;
using Microsoft.Kinect;
using Microsoft.Kinect.Face;

namespace KSIM
{
    public class SKEventArgs : EventArgs
    {
        public long Timestamp
        {
            get;
        }

        public SKEventArgs(long timestamp)
        {
            Timestamp = timestamp;
        }
    }

    public class SKMultiSourceFrameArrivedEventArgs : SKEventArgs
    {
        public MultiSourceFrameArrivedEventArgs Args
        {
            get;
        }

        public SKMultiSourceFrameArrivedEventArgs(MultiSourceFrameArrivedEventArgs e, long timestamp) : base(timestamp)
        {
            Args = e;
        }
    }

    public class SKAudioBeamFrameArrivedEventArgs : SKEventArgs
    {
        public AudioBeamFrameArrivedEventArgs Args
        {
            get;
        }

        public SKAudioBeamFrameArrivedEventArgs(AudioBeamFrameArrivedEventArgs e, long timestamp) : base(timestamp)
        {
            Args = e;
        }
    }

    public class SKFaceFrameArrivedEventArgs : SKEventArgs
    {
        public FaceFrameArrivedEventArgs Args
        {
            get;
        }

        public SKFaceFrameArrivedEventArgs(FaceFrameArrivedEventArgs e, long timestamp) : base(timestamp)
        {
            Args = e;
        }
    }
}
