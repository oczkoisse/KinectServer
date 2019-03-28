using KSIM.Frames;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;

namespace KSIM
{
    public abstract class SpeechRecognizer
	{
		/// <summary>
		/// Conversion stream needed to convert the raw 32 bit floating point samples emitted by Kinect into PCM 16 bit data
		/// that can be recognized by the SpeechRecognitionEngine.
		/// Needed to speechActive = false at application exit
		/// </summary>
		protected Pcm16Stream audioStream;

		private readonly ConcurrentQueue<SpeechResult> speechResults = new ConcurrentQueue<SpeechResult>();

		public SpeechRecognizer()
		{
		}

		public abstract bool InitializeSpeech(bool listenFromKinect, string grammarFileName);

		//async write to recognition engine
		//do processing (locally sync)
		//enqueue results when done
		public abstract bool EnqueueAudio();

		//sync dequeue results
		public abstract void DequeueResult();
	}
}
