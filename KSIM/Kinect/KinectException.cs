using System;

namespace KSIM.Kinect
{
    public class KinectException: Exception
    {
        public KinectException(string message) : base(message) { }

        public KinectException(string message, Exception inner) : base(message, inner) { }

        public KinectException() { }
    }
}
