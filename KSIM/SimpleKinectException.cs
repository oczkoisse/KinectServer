using System;

namespace KSIM
{
    public class SimpleKinectException: Exception
    {
        public SimpleKinectException(string message) : base(message) { }

        public SimpleKinectException(string message, Exception inner) : base(message, inner) { }

        public SimpleKinectException() { }
    }
}
