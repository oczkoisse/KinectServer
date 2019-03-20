using System;
using System.IO;
using Microsoft.Kinect;
using Microsoft.Kinect.Face;

namespace KSIM.Frames
{
    class ClosestFaceFrame: Frame
    {
        private FaceFrameResult faceFrameResult;

        private ClosestBodyFrame closestBodyFrame;

        public ClosestFaceFrame(FaceFrameResult faceFrameResult, ClosestBodyFrame closestBodyFrame)
        {
            Type = FrameType.ClosestFace;

            if (faceFrameResult != null && closestBodyFrame != null && faceFrameResult.TrackingId == closestBodyFrame.TrackingId)
            {
                this.closestBodyFrame = closestBodyFrame;
                this.faceFrameResult = faceFrameResult;
                FaceFound = true;
            }
            else
            {
                FaceFound = false;
            }
        }

        public ClosestFaceFrame(): this(null, null)
        {
        }
        
        public bool FaceFound
        {
            get;
        }

        public ulong TrackingId
        {
            get => FaceFound ? faceFrameResult.TrackingId : 0;
        }

        protected override void SerializeMiddle(BinaryWriter writer)
        {
            writer.Write((byte) (FaceFound ? 1 :0));

            if (FaceFound)
            {
                bool engaged = faceFrameResult.FaceProperties[FaceProperty.Engaged] == DetectionResult.Yes;
                bool lookingAway = faceFrameResult.FaceProperties[FaceProperty.LookingAway] == DetectionResult.Yes;
                bool wearingGlasses = faceFrameResult.FaceProperties[FaceProperty.WearingGlasses] == DetectionResult.Yes;

                writer.Write((byte)(engaged ? 1 : 0));
                writer.Write((byte)(lookingAway ? 1 : 0));
                writer.Write((byte)(wearingGlasses ? 1 : 0));

                ExtractFaceRotationInDegrees(faceFrameResult.FaceRotationQuaternion, out double pitch, out double yaw, out double roll);

                writer.Write(pitch);
                writer.Write(yaw);
                writer.Write(roll);
            }
            else
            {
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((byte)0);

                writer.Write(0.0);
                writer.Write(0.0);
                writer.Write(0.0);
            }
        }
        
        /// <summary>
        /// Converts rotation quaternion to Euler angles
        /// </summary>
        /// <param name="rotQuaternion">face rotation quaternion</param>
        /// <param name="pitch">rotation about the X-axis</param>
        /// <param name="yaw">rotation about the Y-axis</param>
        /// <param name="roll">rotation about the Z-axis</param>
        private static void ExtractFaceRotationInDegrees(Vector4 rotQuaternion, out double pitch, out double yaw, out double roll)
        {
            double x = rotQuaternion.X;
            double y = rotQuaternion.Y;
            double z = rotQuaternion.Z;
            double w = rotQuaternion.W;

            // convert face rotation quaternion to Euler angles in degrees
            double yawD, pitchD, rollD;
            pitch = Math.Atan2(2 * ((y * z) + (w * x)), (w * w) - (x * x) - (y * y) + (z * z)) / Math.PI * 180.0;
            yaw = Math.Asin(2 * ((w * y) - (x * z))) / Math.PI * 180.0;
            roll = Math.Atan2(2 * ((x * y) + (w * z)), (w * w) + (x * x) - (y * y) - (z * z)) / Math.PI * 180.0;
        }
    }
}
