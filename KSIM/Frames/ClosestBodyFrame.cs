using System;
using System.Collections.Generic;
using Microsoft.Kinect;
using System.IO;
using System.Diagnostics;

namespace KSIM.Frames
{
    public class ClosestBodyFrame : Frame
    {
        private static double engageBeginBoundZ;
        private static double engageEndBoundZ;


        private Body closestBody = null;

        public byte TrackedCount
        {
            get;
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
                return BodyFound && IsEngaged(this.closestBody);
            }
        }

        public bool BodyFound
        {
            get { return this.closestBody != null; }
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

        private static ulong lastEngagedTrackingId = 0;

        public ClosestBodyFrame(Microsoft.Kinect.BodyFrame bf, double engageMin, double engageMax)
        {
            Type = FrameType.ClosestBody;

			engageBeginBoundZ = engageMin;
			engageEndBoundZ = engageMax;

			Body[] bodies = new Body[bf.BodyCount];
            bf.GetAndRefreshBodyData(bodies);

            double closestDistance = Double.MaxValue;

            for (int i = 0; i < bodies.Length; i++)
            {
                var body = bodies[i];

                if (body.IsTracked)
                    TrackedCount++;

                double distance = DistanceFromKinect(body);

                if (IsEngaged(distance))
                {
                    if (body.TrackingId == lastEngagedTrackingId)
                    {
                        closestBody = body;
                        closestIndex = i;
                        break;
                    }
                    else if (distance < closestDistance)
                    {
                        closestBody = body;
                        closestDistance = distance;
                        closestIndex = i;
                    }
                }
            }

            // Enagaged implies BodyFound 
            Debug.Assert(!Engaged || BodyFound);

            if (!BodyFound)
                lastEngagedTrackingId = 0;
            else if (TrackingId != lastEngagedTrackingId)
            {
                lastEngagedTrackingId = TrackingId;
            }

            //Debug.WriteLine("Number of tracked bodies: {0}", trackedCount);
        }

        protected override void SerializeMiddle(BinaryWriter writer)
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
        }
    }
}
