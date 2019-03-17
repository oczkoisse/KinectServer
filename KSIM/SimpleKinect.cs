using System;
using System.Threading;
using Microsoft.Kinect;
using Microsoft.Kinect.Face;

namespace KSIM
{
    public class SimpleKinectSensor
    {
        [Flags]
        public enum FrameType
        {
            Color = 1,
            Depth = 2,
            Body = 4,
            Face = 8,
            Audio = 16,
            PCMAudio = 32
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

        private KinectSensor sensor;

        private MultiSourceFrameReader multiSourceFrameReader;
        public event EventHandler<SKMultiSourceFrameArrivedEventArgs> MultiSourceFrameArrived;


        private FaceFrameSource[] faceFrameSources;
        private FaceFrameReader[] faceFrameReaders;
        public event EventHandler<SKFaceFrameArrivedEventArgs> FaceFrameArrived;
        
        
        private AudioBeamFrameReader audioFrameReader;
        public event EventHandler<SKAudioBeamFrameArrivedEventArgs> AudioBeamFrameArrived;
        
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

        public SimpleKinectSensor(FrameType frames)
        {
            enabledFrames = frames;

            sensor = KinectSensor.GetDefault();
            if (sensor == null)
                throw new SimpleKinectException("Failed to setup Kinect sensor");

            if (IsFrameTypeEnabled(FrameType.Color) || IsFrameTypeEnabled(FrameType.Depth) || IsFrameTypeEnabled(FrameType.Body))
                InitializeMultiSourceFrameReader(sensor);
            if (IsFrameTypeEnabled(FrameType.Face))
                InitializeFaceFrameReader(sensor);
            if (IsFrameTypeEnabled(FrameType.Audio))
                InitializeAudioFrameReader(sensor);

            sensor.Open();
        }  

        public bool IsFrameTypeEnabled(FrameType frameType) => frameType.HasFlag(frameType);

        private void InitializeMultiSourceFrameReader(KinectSensor sensor)
        {
            if (sensor == null)
                throw new ArgumentNullException("Sensor is null");

            FrameSourceTypes requestedFrames = FrameSourceTypes.None;
            if (IsFrameTypeEnabled(FrameType.Color))
                requestedFrames |= FrameSourceTypes.Color;
            if (IsFrameTypeEnabled(FrameType.Depth))
                requestedFrames |= FrameSourceTypes.Depth;
            if (IsFrameTypeEnabled(FrameType.Body))
                requestedFrames |= FrameSourceTypes.Body;

            multiSourceFrameReader = sensor.OpenMultiSourceFrameReader(requestedFrames);

            multiSourceFrameReader.MultiSourceFrameArrived += OnMultiSourceFrameArrived;
        }
        
        private void OnMultiSourceFrameArrived(object sender, MultiSourceFrameArrivedEventArgs e)
        {
            Timestamp = DateTime.Now.Ticks;
            MultiSourceFrameArrived?.Invoke(sender, new SKMultiSourceFrameArrivedEventArgs(e, Timestamp));
        }

        private void InitializeAudioFrameReader(KinectSensor sensor)
        {
            if (sensor == null)
                throw new ArgumentNullException("Sensor is null");

            audioFrameReader = sensor.AudioSource.OpenReader();

            var beams = sensor.AudioSource.AudioBeams;
            if (beams == null || beams.Count == 0)
                throw new SimpleKinectException("Unable to retrieve audio beams from Kinect sensor");

            this.AudioBeam = beams[0];

            audioFrameReader.FrameArrived += OnAudioFrameArrived;
        }

        private void OnAudioFrameArrived(object sender, AudioBeamFrameArrivedEventArgs e)
        {
            AudioBeamFrameArrived?.Invoke(sender, new SKAudioBeamFrameArrivedEventArgs(e, Timestamp));
        }
        

        private void InitializeFaceFrameReader(KinectSensor sensor)
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

        private void OnFaceFrameArrived(object sender, FaceFrameArrivedEventArgs e)
        {
            FaceFrameArrived?.Invoke(sender, new SKFaceFrameArrivedEventArgs(e, Timestamp));
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
