using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Kinect;
using System.Diagnostics;

namespace KSIM.Readers
{
    class AudioReader : Reader
    {
        public override Frame Read(MultiSourceFrame f)
        {
            throw new NotImplementedException("AudioReader cannot read from a MultiSourceFrame. Pass AudioBeamFrame instead.");
        }

        public override Frame Read(AudioBeamFrameList lf)
        {
            if (lf != null && lf.Count > 0)
            {
                // Possible that there is no new audio
                // Still must have a dummy audio frame
                // unless there is a bigger problem like having zero audio beams
                // TO CHECK: How often does this list be null? You don't want to skip other frames beacuse of this
                var af =  new AudioFrame(lf);
                if (af != null)
                    return af;
                else
                    lf.Dispose();
            }
            return null;
        }
        
    }

    class AudioFrame : Frame
    {
        private bool disposed = false;

        private AudioBeamFrameList beamFrameList = null;

        private AudioBeamFrame UnderlyingAudioFrame
        {
            get
            {
                return beamFrameList[0];
            }
        }

        private float[] audioBuffer = null;

        private int subFrameCount = 0;

        public int SubFrameCount
        {
            get { return subFrameCount; }
        }

        public AudioFrame(Microsoft.Kinect.AudioBeamFrameList lf)
        {
            this.Type = FrameType.Audio;
            // AudioBeamFrame indexed by AudioBeams
            // Logically, each AudioBeam represents one of the mics in the microphone array
            // But the API limits us to only 1 AudioBeam, the cumulative one, which is at index 0
            this.beamFrameList = lf;
            this.subFrameCount = UnderlyingAudioFrame.SubFrames.Count;

            Debug.Assert(this.subFrameCount != 0);
            // The following is always 1024 bytes
            byte[] subrameBuffer = new byte[UnderlyingAudioFrame.AudioSource.SubFrameLengthInBytes];

            // To store floating point samples associated with thie frame
            this.audioBuffer = new float[(UnderlyingAudioFrame.AudioSource.SubFrameLengthInBytes / sizeof(float)) * UnderlyingAudioFrame.SubFrames.Count];

            // Allocate 1024 bytes to hold a single audio sub frame. Duration sub frame 
            // is 16 msec, the sample rate is 16khz, which means 256 samples per sub frame. 
            // With 4 bytes per sample, that gives us 1024 bytes.
            // Note that Kinect Audio is mono sampled at 16kHz.
            // Beam angle may vary among subframes belonging to the same AudioBeamFrame
            foreach (var sframe in UnderlyingAudioFrame.SubFrames)
            {
                // May want to filter out subframes with low confidence for beam angle
                sframe.CopyFrameDataToArray(subrameBuffer);
                for (int i = 0, j = 0; i < subrameBuffer.Length; i += sizeof(float), j++)
                    audioBuffer[j] = BitConverter.ToSingle(subrameBuffer, i);
            }
        }
            
           
        public override void Serialize(Stream s)
        {
            using (BinaryWriter writer = new BinaryWriter(s))
            {
                int loadSize = 0;
                writer.Write(loadSize);
                writer.Write(Timestamp);
                writer.Write(1 << (int)Type);

                // Note that each subframe is fixed size -- 1024 bytes
                writer.Write(audioBuffer.Length);

                for(int i = 0; i < audioBuffer.Length; i++)
                {
                    writer.Write(audioBuffer[i]);
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
                    if (beamFrameList != null)
                        beamFrameList.Dispose();
                }
            }
            disposed = true;
        }
    }
}
