using System;
using System.Threading;
using Microsoft.Kinect;
using Microsoft.Kinect.Face;

namespace KSIM.Kinect
{
    public class KinectSensor
    {
        [Flags]
        public enum FrameType
        {
            Color = 1,
            Infrared = 2,
            Depth = 8,
            BodyIndex = 16,
            Body = 32,
            Audio = 64,
            Face = 128
        }
        
        private readonly FrameType enabledFrames;
        private readonly FaceFrameFeatures faceFrameFeatures = FaceFrameFeatures.BoundingBoxInColorSpace
            | FaceFrameFeatures.BoundingBoxInInfraredSpace
            | FaceFrameFeatures.PointsInColorSpace
            | FaceFrameFeatures.PointsInInfraredSpace
            | FaceFrameFeatures.RotationOrientation
            | FaceFrameFeatures.LeftEyeClosed
            | FaceFrameFeatures.RightEyeClosed
            | FaceFrameFeatures.Glasses
            | FaceFrameFeatures.FaceEngagement
            | FaceFrameFeatures.MouthMoved
            | FaceFrameFeatures.MouthOpen
            | FaceFrameFeatures.LookingAway;

        private Microsoft.Kinect.KinectSensor sensor;

        private Body[] bodies;
        
        private MultiSourceFrameReader multiSourceFrameReader;
        public event EventHandler<MultiSourceFrameArrivedEventArgs> MultiSourceFrameArrived;


        private FaceFrameSource[] faceFrameSources;
        private FaceFrameReader[] faceFrameReaders;
        public event EventHandler<FaceFrameArrivedEventArgs> FaceFrameArrived;
        
        
        private AudioBeamFrameReader audioFrameReader;
        public event EventHandler<AudioBeamFrameArrivedEventArgs> AudioBeamFrameArrived;
        
        public AudioBeam AudioBeam
        {
            get;
            private set;
        }

        private long timestamp = long.MinValue;

        public long Timestamp
        {
            get => Interlocked.Read(ref timestamp);
            private set => Interlocked.Exchange(ref timestamp, value);
        }

        public int MaxTrackedBodies
        {
            get => sensor?.BodyFrameSource.BodyCount ?? 0;
            
        }

        public FrameDescription ColorFrameDescription
        {
            get => sensor?.ColorFrameSource.FrameDescription;
        }

        public FrameDescription DepthFrameDescription
        {
            get => sensor?.DepthFrameSource.FrameDescription;
        }

        public CoordinateMapper CoordinateMapper
        {
            get => sensor?.CoordinateMapper;
        }

        public KinectSensor(FrameType frames)
        {
            enabledFrames = frames;

            sensor = Microsoft.Kinect.KinectSensor.GetDefault();
            if (sensor == null)
                throw new KinectException("Failed to setup Kinect sensor");

            if (IsFrameTypeEnabled(FrameType.Color) || IsFrameTypeEnabled(FrameType.Infrared) || IsFrameTypeEnabled(FrameType.Depth)
                || IsFrameTypeEnabled(FrameType.BodyIndex) || IsFrameTypeEnabled(FrameType.Body))
            {
                InitializeMultiSourceFrameReader(sensor);
            }

            if (IsFrameTypeEnabled(FrameType.Audio))
                InitializeAudioFrameReader(sensor);

            if (IsFrameTypeEnabled(FrameType.Face))
                InitializeFaceFrameReader(sensor);

            sensor.Open();
        }  

        public bool IsFrameTypeEnabled(FrameType frameType) => frameType.HasFlag(frameType);

        private void InitializeMultiSourceFrameReader(Microsoft.Kinect.KinectSensor sensor)
        {
            if (sensor == null)
                throw new ArgumentNullException("Sensor is null");

            bodies = new Body[MaxTrackedBodies];

            FrameSourceTypes requestedFrames = FrameSourceTypes.None;
            if (IsFrameTypeEnabled(FrameType.Color))
                requestedFrames |= FrameSourceTypes.Color;
            if (IsFrameTypeEnabled(FrameType.Depth))
                requestedFrames |= FrameSourceTypes.Depth;
            if (IsFrameTypeEnabled(FrameType.Body))
                requestedFrames |= FrameSourceTypes.Body;
            if (IsFrameTypeEnabled(FrameType.Infrared))
                requestedFrames |= FrameSourceTypes.Infrared;
            if (IsFrameTypeEnabled(FrameType.BodyIndex))
                requestedFrames |= FrameSourceTypes.BodyIndex;

            multiSourceFrameReader = sensor.OpenMultiSourceFrameReader(requestedFrames);

            multiSourceFrameReader.MultiSourceFrameArrived += OnMultiSourceFrameArrived;
        }
                
        private void OnMultiSourceFrameArrived(object sender, Microsoft.Kinect.MultiSourceFrameArrivedEventArgs e)
        {
            long timestamp = DateTime.Now.Ticks;
            Timestamp = timestamp;

            var multiSourceFrame = e.FrameReference.AcquireFrame();

            ColorFrame colorFrame = null;
            DepthFrame depthFrame = null;
            BodyFrame bodyFrame = null;
            InfraredFrame infraredFrame = null;
            BodyIndexFrame bodyIndexFrame = null;

            try
            {
                colorFrame = multiSourceFrame.ColorFrameReference.AcquireFrame();
                depthFrame = multiSourceFrame.DepthFrameReference.AcquireFrame();

                bodyFrame = multiSourceFrame.BodyFrameReference.AcquireFrame();
                bodyFrame?.GetAndRefreshBodyData(bodies);

                infraredFrame = multiSourceFrame.InfraredFrameReference.AcquireFrame();
                bodyIndexFrame = multiSourceFrame.BodyIndexFrameReference.AcquireFrame();

                MultiSourceFrameArrived?.Invoke(this, new MultiSourceFrameArrivedEventArgs(timestamp, colorFrame, depthFrame,
                    bodyFrame, bodyIndexFrame, infraredFrame));
            }
            finally
            {
                colorFrame?.Dispose();
                depthFrame?.Dispose();
                bodyFrame?.Dispose();
                bodyIndexFrame?.Dispose();
                infraredFrame?.Dispose();
            }
        }

        private void InitializeAudioFrameReader(Microsoft.Kinect.KinectSensor sensor)
        {
            if (sensor == null)
                throw new ArgumentNullException("Sensor is null");

            audioFrameReader = sensor.AudioSource.OpenReader();

            var beams = sensor.AudioSource.AudioBeams;
            if (beams == null || beams.Count == 0)
                throw new KinectException("Unable to retrieve audio beams from Kinect sensor");

            this.AudioBeam = beams[0];

            audioFrameReader.FrameArrived += OnAudioFrameArrived;
        }

        private void OnAudioFrameArrived(object sender, Microsoft.Kinect.AudioBeamFrameArrivedEventArgs e)
        {
            AudioBeamFrameList audioBeamFrameList = null;
            long timestamp = Timestamp;
            try
            {
                audioBeamFrameList = e.FrameReference.AcquireBeamFrames();
                AudioBeamFrameArrived?.Invoke(this, new AudioBeamFrameArrivedEventArgs(timestamp, audioBeamFrameList));
            }
            finally
            {
                audioBeamFrameList?.Dispose();
            }
        }
        

        private void InitializeFaceFrameReader(Microsoft.Kinect.KinectSensor sensor)
        {
            if (sensor == null)
                throw new ArgumentNullException("Sensor is null");

            faceFrameSources = new FaceFrameSource[MaxTrackedBodies];
            faceFrameReaders = new FaceFrameReader[MaxTrackedBodies];

            for (int i=0; i<MaxTrackedBodies; i++)
            {
                faceFrameSources[i] = new FaceFrameSource(sensor, 0, faceFrameFeatures);
                faceFrameReaders[i] = faceFrameSources[i].OpenReader();

                faceFrameReaders[i].FrameArrived += OnFaceFrameArrived;
            }
        }

        private bool TryGetFaceFrameSourceIndex(FaceFrameSource faceFrameSource, out int index)
        {
            for (int i=0; i < faceFrameSources.Length; i++)
            {
                if (faceFrameSources[i] == faceFrameSource)
                {
                    index = i;
                    return true;
                }
            }
            index = -1;
            return false;
        }

        private void OnFaceFrameArrived(object sender, Microsoft.Kinect.Face.FaceFrameArrivedEventArgs e)
        {
            long timestamp = Timestamp;
            FaceFrame faceFrame = null;
            try
            {
                faceFrame = e.FrameReference.AcquireFrame();
                if (TryGetFaceFrameSourceIndex(faceFrame.FaceFrameSource, out int index))
                {
                    if (faceFrameSources[index].IsTrackingIdValid)
                    {
                        FaceFrameArrived?.Invoke(this, new FaceFrameArrivedEventArgs(timestamp, faceFrame.FaceFrameResult));
                    }
                    else if (bodies[index] != null && bodies[index].IsTracked)
                    {
                        faceFrameSources[index].TrackingId = bodies[index].TrackingId;
                    }
                }
            }
            finally
            {
                faceFrame?.Dispose();
            }
        }

        public void Close()
        {
            if (multiSourceFrameReader != null)
            {
                multiSourceFrameReader.MultiSourceFrameArrived -= OnMultiSourceFrameArrived;
                multiSourceFrameReader.Dispose();
                multiSourceFrameReader = null;
            }
            
            if (audioFrameReader != null)
            {
                audioFrameReader.FrameArrived -= OnAudioFrameArrived;
                audioFrameReader.Dispose();
                audioFrameReader = null;
            }

            for (int i = 0; i < MaxTrackedBodies; i++)
            {
                if (faceFrameReaders[i] != null)
                {
                    faceFrameReaders[i].FrameArrived -= OnFaceFrameArrived;
                    faceFrameReaders[i].Dispose();
                    faceFrameReaders[i] = null;
                }

                if (faceFrameSources[i] != null)
                {
                    faceFrameSources[i].Dispose();
                    faceFrameSources[i] = null;
                }
            }

            if (sensor != null)
            {
                sensor.Close();
                sensor = null;
            }
        }
    }
}
