using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KSIM
{
    public class SimpleKinectException: Exception
    {
        public SimpleKinectException(string message) : base(message) { }

        public SimpleKinectException(string message, Exception inner) : base(message, inner) { }

        public SimpleKinectException() { }
    }
}
