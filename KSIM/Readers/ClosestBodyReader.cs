using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Kinect;
using System.IO;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace KSIM.Readers
{
    public sealed class ClosestBodyReader : Reader
    {
        public override Frame Read(MultiSourceFrame f)
        {
            // Note that we do not dispose the acquired frame
            // that responsibility is delegated to newly created frame
            var originalFrame = f.BodyFrameReference.AcquireFrame();

            if (originalFrame != null)
            {
                return new ClosestBodyFrame(originalFrame);
            }
            
            return null;
        }
    }

    public class ClosestBodyFrame : Frame
    {
        private bool disposed = false;

        private readonly static double engageBeginBoundZ = 1.5;
        private readonly static double engageEndBoundZ = 5.0;


        private Body closestBody = null;
        protected BodyFrame underlyingBodyFrame = null;

        private byte trackedCount = 0;

        public byte TrackedCount
        {
            get
            {
                return trackedCount;
            }
        }

        private static bool IsEngaged(Body body)
        {
            double distance = DistanceFromKinect(body);
            return IsEngaged(distance);
        }

        private static bool IsEngaged(double distance)
        {
            return distance > engageBeginBoundZ && distance < engageEndBoundZ;
        }

        private static double DistanceFromKinect(Body body)
        {
            if (!body.IsTracked)
                return Double.MaxValue;

            var pos = body.Joints[JointType.SpineBase].Position;
            return Math.Pow(pos.X, 2) + Math.Pow(pos.Y, 2) + Math.Pow(pos.Z, 2);
        }

        public bool Engaged
        {
            get
            {
                return this.closestBody != null && IsEngaged(this.closestBody);
            }
        }

        public bool BodyFound
        {
            get { return trackedCount > 0; }
        }

        public ulong TrackingId
        {
            get
            {
                return BodyFound ? closestBody.TrackingId : 0;
            }
        }

        public TrackingConfidence HandLeftConfidence
        {
            get
            {
                return BodyFound ? closestBody.HandLeftConfidence: TrackingConfidence.Low;
            }
        }

        public TrackingConfidence HandRightConfidence
        {
            get
            {
                return BodyFound ? closestBody.HandLeftConfidence : TrackingConfidence.Low;
            }
        }

        public HandState HandLeftState
        {
            get
            {
                return BodyFound ? closestBody.HandLeftState : HandState.NotTracked;
            }
        }

        public HandState HandRightState
        {
            get
            {
                return BodyFound ? closestBody.HandRightState : HandState.NotTracked;
            }
        }

        public IReadOnlyDictionary<JointType, Joint> Joints
        {
            get { return BodyFound ? closestBody.Joints : null; }
        }

        public IReadOnlyDictionary<JointType, JointOrientation> JointOrientations
        {
            get { return BodyFound ? closestBody.JointOrientations : null; }
        }

        private int closestIndex;
        public int Index
        {
            get { return BodyFound ? closestIndex : -1; }
        }

        

        public ClosestBodyFrame(Microsoft.Kinect.BodyFrame bf)
        {
            Type = FrameType.ClosestBody;
            this.underlyingBodyFrame = bf;
            
            Body[] bodies = new Body[bf.BodyCount];
            bf.GetAndRefreshBodyData(bodies);

            double closestDistance = Double.MaxValue;

            for (int i = 0; i < bodies.Length; i++)
            {
                var body = bodies[i];

                if (body.IsTracked)
                    trackedCount++;

                double distance = DistanceFromKinect(body);

                if (IsEngaged(distance) && distance < closestDistance)
                {

                    closestBody = body;
                    closestDistance = distance;
                    closestIndex = i;
                }
            }
            // Enagaged implies BodyFound 
            Debug.Assert(!Engaged || BodyFound);

            //Debug.WriteLine("Number of tracked bodies: {0}", trackedCount);
        }

        public override void Serialize(Stream s)
        {
            // Format:
            // Load Size (4 bytes, signed) | Timestamp (8 bytes, signed) | Frame Type (4 bytes, bitset) | Tracked Body Count (1 byte, unsigned)
            // Tracking ID (8 bytes, unsigned) | Hand Left Confidence (1 byte, unsigned) | Hand Left State (1 byte, unsigned)
            // Hand Right Confidence (1 byte, unsigned) | Hand Right State (1 byte, unsigned)
            // For each Joint, X, Y, Z, W, X, Y, Z (all floats)
            //
            // Description:
            // Load Size: Number of bytes to be read further to read one DepthFrame
            // Timestamp: For syncing frames at the client side
            // Tracked Body Count: Number of bodies currently tracke by Kinect
            // Tracking ID: The Tracking ID assigned by Kinect to the closest body
            // Hand Left Confidence:
            // Hand Left State:
            // Hand Right Confidence:
            // Hand Right State:
            // All of the joints of the closest body in the format:
            //     Joint Type:
            //     Joint Tracking State:
            //     Joint Position X, Y, Z
            //     Joint Orientation W, X, Y, Z

            // Note that BinaryWriter is documented to write data in little-endian form only
            using (BinaryWriter writer = new BinaryWriter(s))
            {
                int loadSize = 0;
                // Placeholder for load size to be filled in later
                writer.Write(loadSize);

                writer.Write(Timestamp);
                writer.Write(1 << (int)Type);

                writer.Write(TrackedCount);
                writer.Write(Engaged);

                if (Engaged)
                {
                    writer.Write(TrackingId);

                    writer.Write((byte)HandLeftConfidence);
                    writer.Write((byte)HandLeftState);

                    writer.Write((byte)HandRightConfidence);
                    writer.Write((byte)HandRightState);
                    
                    // Assume we'll always have orientation for a joint that has a position
                    foreach (JointType j in Joints.Keys)
                    {
                        Joint joint = Joints[j];
                        JointOrientation jointOrient = JointOrientations[j];

                        writer.Write((byte)j);
                        writer.Write((byte)joint.TrackingState);

                        writer.Write(joint.Position.X);
                        writer.Write(joint.Position.Y);
                        writer.Write(joint.Position.Z);

                        writer.Write(jointOrient.Orientation.W);
                        writer.Write(jointOrient.Orientation.X);
                        writer.Write(jointOrient.Orientation.Y);
                        writer.Write(jointOrient.Orientation.Z);
                    }
                }

                // Rewind back to write the load size in the first 4 bytes
                loadSize = (int)writer.Seek(0, SeekOrigin.Current) - sizeof(int);
                writer.Seek(0, SeekOrigin.Begin);
                writer.Write(loadSize);
                writer.Seek(0, SeekOrigin.End);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects).
                    if (underlyingBodyFrame != null)
                        underlyingBodyFrame.Dispose();
                }
            }
            disposed = true;
        }

    }
}
