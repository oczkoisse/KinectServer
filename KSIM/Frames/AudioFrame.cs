using System;
using System.IO;
using Microsoft.Kinect;
using System.Diagnostics;

namespace KSIM.Frames
{
    class AudioFrame : Frame
    {
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
            int j = 0;
            foreach (var sframe in UnderlyingAudioFrame.SubFrames)
            {
                //Debug.Write(sframe.BeamAngleConfidence.ToString() + "\n");
                if (sframe.BeamAngleConfidence >= 0.8)
                {
                    // May want to filter out subframes with low confidence for beam angle
                    sframe.CopyFrameDataToArray(subrameBuffer);
                    for (int i = 0; i < subrameBuffer.Length; i += sizeof(float), j++)
                        audioBuffer[j] = BitConverter.ToSingle(subrameBuffer, i);
                }
            }
        }
            
           
        protected override void SerializeMiddle(BinaryWriter writer)
        {
            // Note that each subframe is fixed size -- 1024 bytes
            writer.Write(audioBuffer.Length);

            for (int i = 0; i < audioBuffer.Length; i++)
            {
                writer.Write(audioBuffer[i]);
            }
        }
    }
}
