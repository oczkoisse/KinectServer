using System;
using System.Globalization;
using System.IO;
using Microsoft.Speech.AudioFormat;
using Microsoft.Speech.Recognition;
using Microsoft.Speech.Recognition.SrgsGrammar;

namespace KSIM
{
	public class MicrosoftSpeechRecognizer : SpeechRecognizer
	{
		/// <summary>
		/// Reference to the SpeechRecognitionEngine. Needed to stop async recogntion at the application exit.
		/// </summary>
		private static SpeechRecognitionEngine speechEngine;

		public override bool InitializeSpeech(bool listenFromKinect, string grammarFileName)
		{
			Grammar g = LoadGrammar(grammarFileName, true);
			
			if (g != null)
			{
				if (listenFromKinect)
				{
					RecognizerInfo ri = TryGetKinectRecognizer();
					if (null == ri)
					{
						Console.Error.WriteLine("Cannot initiate Kinect microphone, is a Kinect (v2) plugged in?");
						return false;
					}
					speechEngine = new SpeechRecognitionEngine(ri.Id);
					speechEngine.SetInputToAudioStream(audioStream, new SpeechAudioFormatInfo(EncodingFormat.Pcm, 16000, 16, 1, 32000, 2, null));
				}
				else
				{
					CultureInfo ci = new CultureInfo("en-US");
					if (ci != null)
					{
						speechEngine = new SpeechRecognitionEngine(ci);
						speechEngine.SetInputToDefaultAudioDevice();
					}
				}

				speechEngine.LoadGrammar(g);
				speechEngine.RecognizeAsync(RecognizeMode.Multiple);
				speechEngine.SpeechRecognized += Reader_SpeechRecognized;
				return true;
			}
			else
				return false;
		}

		private void Reader_SpeechRecognized(object sender, SpeechRecognizedEventArgs e)
		{
			var result = e.Result;

			textBox.AppendText($"Phrase \"{result.Text}\" (confidence: {result.Confidence})\n");
			textBox.AppendText($"{result.Semantics["Tag"].Value},{result.Text}\n");
			textBox.ScrollToEnd();

			listSpeechFrame.Enqueue(new SpeechFrame(result));
		}

		/// <summary>
		/// Gets the metadata for the speech recognizer (acoustic model) most suitable to
		/// process audio from Kinect device.
		/// </summary>
		/// <returns>
		/// RecognizerInfo if found, <code>null</code> otherwise.
		/// </returns>
		private static RecognizerInfo TryGetKinectRecognizer()
		{
			IEnumerable<RecognizerInfo> recognizers;

			// This is required to catch the case when an expected recognizer is not installed.
			// By default - the x86 Speech Runtime is always expected. 
			try
			{
				recognizers = SpeechRecognitionEngine.InstalledRecognizers();
			}
			catch (System.Runtime.InteropServices.COMException)
			{
				return null;
			}

			foreach (RecognizerInfo recognizer in recognizers)
			{
				string value;
				recognizer.AdditionalInfo.TryGetValue("Kinect", out value);
				if ("True".Equals(value, StringComparison.OrdinalIgnoreCase) && "en-US".Equals(recognizer.Culture.Name, StringComparison.OrdinalIgnoreCase))
				{
					return recognizer;
				}
			}

			return null;
		}

		static Grammar LoadGrammar(string grammarPathString, bool forceCompile)
		{
			if (grammarPathString == null)
			{
				return null;
			}

			string compiledGrammarPathString;
			string grammarExtension = Path.GetExtension(grammarPathString);
			if (grammarExtension.Equals(".grxml", StringComparison.OrdinalIgnoreCase))
			{
				compiledGrammarPathString = Path.ChangeExtension(grammarPathString, "cfg");
			}
			else if (grammarExtension.Equals(".cfg", StringComparison.OrdinalIgnoreCase))
			{
				compiledGrammarPathString = grammarPathString;
			}
			else
			{
				throw new FormatException("Grammar file format is unsupported: " + grammarExtension);
			}

			// skip cpmpilation if "cfg" grammar already exists
			if (forceCompile || !File.Exists(compiledGrammarPathString))
			{
				FileStream fs = new FileStream(compiledGrammarPathString, FileMode.Create);
				var srgs = new SrgsDocument(grammarPathString);
				SrgsGrammarCompiler.Compile(srgs, fs);
				fs.Close();
			}

			return new Grammar(compiledGrammarPathString);
		}

		public override void RecognizeSpeech()
		{
			throw new NotImplementedException();
		}
	}
}
