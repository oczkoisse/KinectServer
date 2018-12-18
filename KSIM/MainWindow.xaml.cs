﻿using KSIM.Readers;
using Microsoft.Kinect;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Threading;
using Microsoft.Speech.AudioFormat;
using Microsoft.Speech.Recognition;
using System.Globalization;
using System.Text;
using System.Windows.Controls;
using Microsoft.Speech.Recognition.SrgsGrammar;
using Mono.Options;

namespace KSIM
{
    /// <summary>
    /// Class holding the interaction logic for MainWindow.xaml.
    /// In addition, this class also handles new incoming client stream requests and
    /// interfaces with the underlying Kinect hardware to facilitate these stream requests.
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// The port at which the application listens for incoming stream requests for Kinect clients
        /// </summary>
        private static int port = 8000;

        private static bool listenFromKinect = false;

        private string _grammarFile = "defaultGrammar.grxml";

        /// <summary>
        /// Reference to the Kinect sensor. Needed to Close() at the application exit.
        /// </summary>
        private static KinectSensor sensor;

        /// <summary>
        /// The server object to listen for incoming client requests
        /// </summary>
        private TcpListener server = new TcpListener(IPAddress.Any, port);
        /// <summary>
        /// A dictionary for holding stream types subscribed by each client. The subscribed streams should not contain Audio or Speech
        /// </summary>
        private Dictionary<TcpClient, List<Readers.FrameType>> connectedClients = new Dictionary<TcpClient, List<Readers.FrameType>>();

        /// <summary>
        /// A dictionary for holding clients subscribed to Audio stream. These clients cannot be subscribed to any other stream.
        /// </summary>
        private List<TcpClient> connectedAudioClients = new List<TcpClient>();

        

        /// <summary>
        /// Reference to the MultiSourceFrameReader reader got from the Kinect sensor. Needed to Dispose() at the application exit.
        /// </summary>
        private MultiSourceFrameReader multiSourceFrameReader = null;

        /// <summary>
        /// Reference to the AudioBeamFrameReader reader got from the Kinect sensor. Needed to Dispose() at the application exit.
        /// </summary>
        private AudioBeamFrameReader audioFrameReader = null;

        /// <summary>
        /// Conversion stream needed to convert the raw 32 bit floating point samples emitted by Kinect into PCM 16 bit data
        /// that can be recognized by the SpeechRecognitionEngine.
        /// Needed to speechActive = false at application exit
        /// </summary>
        private Pcm16Stream audioStream;

        /// <summary>
        /// Reference to the SpeechRecognitionEngine. Needed to stop async recogntion at the application exit.
        /// </summary>
        private static SpeechRecognitionEngine speechEngine;

        // To keep sync between MultiSouceFrame, AudioBeamFrame and Speech stream
        // Note that we cannot ensure perfect sync between AudioBeamFrame and Speech stream
        private long lastTimestamp;

        /// <summary>
        /// Provides an atomic read/write for the last timestamp set in <see cref="Reader_MultiSourceFrameArrived(object, MultiSourceFrameArrivedEventArgs)"/>
        /// </summary>
        /// <value>
        /// The timestamp set in <see cref="Reader_MultiSourceFrameArrived(object, MultiSourceFrameArrivedEventArgs)"/> the last time that event was fired
        /// </value>
        /// <remarks>
        /// This still cannot ensure perfect sync between <see cref="Reader_AudioFrameArrived(object, AudioBeamFrameArrivedEventArgs)"/> 
        /// and <see cref="Reader_SpeechRecognized(object, SpeechRecognizedEventArgs)"/>, since those operate in their own event threads.
        /// </remarks>
        private long LastTimestamp
        {
            get { return Interlocked.Read(ref lastTimestamp); }
            set { Interlocked.Exchange(ref lastTimestamp, value); }
        }

        /// <summary>
        /// Handles incoming client connections. The current implementation verifies these requests by
        /// reading first 4 bytes sent by the client, which are are validated by <see cref="GetActiveFrames(byte[])"/> into a list of stream types.
        /// The client is accepted as a valid subscriber if and only if one of the following is true:
        /// <list type="bullet">
        /// <item> The client request only constitutes the Audio stream type</item>
        /// <item> The client request only constitutes the Speech stream type</item>
        /// <item> The client request contains one or more stream types other than Audio and Speech</item>
        /// </list> 
        /// </summary>
        /// <param name="res"></param>
        private void HandleNewClient(IAsyncResult res)
        {
            TcpClient c = null;
            try
            {
                c = server.EndAcceptTcpClient(res);
            }
            catch(ObjectDisposedException)
            {
                Debug.Write("Server closed while still listening.");
            }
            
            // Server has been stopped so don't listen anymore
            if (c == null)
                return;

            // Else go back to accepting connections
            ContinueAcceptConnections();


            // Read first four bytes of this client
            byte[] requestedFrames = new byte[] { 0x0, 0x0, 0x0, 0x0 };
            int bytesRead = c.GetStream().Read(requestedFrames, 0, 4);
            Debug.Assert(bytesRead == 4);

            // Get a list of valid stream types from these 4 bytes
            List<Readers.FrameType> activeFrames = GetActiveFrames(requestedFrames);

            // If there are one or more valid stream requests
            if (activeFrames.Count >= 1)
            {
                // If the request does not involve Audio
                if (!activeFrames.Contains(FrameType.Audio))
                {
                    // Add the client to list of connected clients other than audio clients
                    lock (connectedClients)
                    {
                        connectedClients.Add(c, activeFrames);
                        Trace.WriteLine(String.Format("Accepted connection from {0}", c.Client.RemoteEndPoint.ToString()));
                        foreach (var ft in activeFrames)
                            Trace.WriteLine((int)ft);
                    }
                }
                // If the request contains only 1 stream type which is Audio
                else if (activeFrames.Count == 1 && activeFrames[0] == FrameType.Audio)
                {
                    lock (connectedAudioClients)
                    {
                        connectedAudioClients.Add(c);
                        Trace.WriteLine(String.Format("Accepted connection from {0}", c.Client.RemoteEndPoint.ToString()));
                        Trace.WriteLine((int)activeFrames[0]);
                    }
                }
                else
                {
                    // Reject as an invalid stream as the stream contains a combination of Audio/Speech with other stream types
                    Trace.WriteLine(String.Format("Rejecting client {0} because it is not possible to combine Audio stream with other streams.", c.Client.RemoteEndPoint.ToString()));
                    c.Close();
                }
            }
            else
            {
                // Reject as no valid stream was identified
                Trace.WriteLine(String.Format("Rejecting client {0} because of an unrecognized stream type(s)", c.Client.RemoteEndPoint.ToString()));
                c.Close();
            }
        }


        /// <summary>
        /// Decodes the input bytes into a list of frame types. Currently, bits 1-31 are the only ones considered, which correspond to frame types as follows:
        /// <list type="table">
        /// <listheader>
        /// <term>Bit position</term>
        /// <description>Frame type</description>
        /// </listheader>
        /// <item>
        /// <term>1</term>
        /// <description>Color</description>
        /// <term>2</term>
        /// <description>Speech</description>
        /// <term>3</term>
        /// <description>Audio</description>
        /// <term>4</term>
        /// <description>Depth</description>
        /// <term>5</term>
        /// <description>Closest Body</description>
        /// <term>6</term>
        /// <description>Left Hand Depth</description>
        /// <term>7</term>
        /// <description>Right Hand Depth</description>
        /// <term>8</term>
        /// <description>Head Depth</description>
        /// </item>
        /// </list>
        /// </summary>
        /// <param name="frameBytes"></param>
        /// <returns>A list of frame types as decoded from the input bytes</returns>
        private List<Readers.FrameType> GetActiveFrames(byte[] frameBytes)
        {
            List<Readers.FrameType> activeFrames = new List<Readers.FrameType>();
            // BitArray assumes these 4 bytes are little endian
            // i.e. first byte corresponds to bits 0-7
            BitArray reqFramesAsBits = new BitArray(frameBytes);

            // Determine which stream bits are active
            foreach (Readers.FrameType ft in Enum.GetValues(typeof(Readers.FrameType)).Cast<Readers.FrameType>())
            {
                if (reqFramesAsBits.Get((int)ft))
                    activeFrames.Add(ft);
            }

            return activeFrames;
        }


        /// <summary>
        /// Starts an asynchronous client accept operation
        /// </summary>
        private void ContinueAcceptConnections()
        {
            try
            {
                server.BeginAcceptTcpClient(HandleNewClient, null);
            }
            catch (Exception e)
            {
                Trace.WriteLine(String.Format("Problem in accepting a TCP connection request: {0}", e.Message));
            }
        }

        /// <summary>
        /// The constructor for MainWindow class. Sets the default value for <see cref="LastTimestamp"/> as minimum possible for 64-bit signed integer.
        /// Initializes the Kinect sensor, along with different native frame readers. 
        /// If successfull, also starts a server for accepting client requests for different streams.
        /// Finally, setups the window controls
        /// </summary>
        public MainWindow()
        {
            String[] args = Environment.GetCommandLineArgs();
            
            bool showOptions = false;
            var p = new OptionSet
            {
                {
                    "k|kinect", "use kinect microphone",
                    v => listenFromKinect = v != null
                },
                {
                    "p=|port=", "port number to use to send kinect streams. (default: 8000)",
                    v =>  port = Int32.Parse(v)
                },
                {
                    "g=|grammar=", "grammar file name to use for speech (cfg or grxml, default: defaultGrammar.grxml).",
                    v => this._grammarFile = v == null ?  "defaultGrammar.grxml" : v
                },
                {
                    "h|help", "show this message",
                    v => showOptions = v != null
                }
            };

            try
            {
                p.Parse(args);
            }
            catch (OptionException e)
            {
                Console.Write("Error: ");
                Console.WriteLine(e.Message);
                Application.Current.Shutdown();
                return;
            }

            if (showOptions)
            {
                Console.WriteLine("Options:");
                p.WriteOptionDescriptions(Console.Out);
                Application.Current.Shutdown();
                return;
            }

            if (!File.Exists(_grammarFile))
            {
                Console.WriteLine("Error: {0}", @"Unable to find grammar file {_grammarFile}");
                Application.Current.Shutdown();
                return;
            }



            LastTimestamp = Int64.MinValue;
            bool success = InitializeKinect();
            if (success)
            {
                server.Start();
                ContinueAcceptConnections();
                InitializeComponent();
                textBox.Clear();
                textBox.AppendText(string.Format("App started at port {0} using {1} microphone and {2} grammar",
                    port, listenFromKinect? "kinect" : "normal", _grammarFile));

            }
        }

        class TextBoxWriter : TextWriter
        {

            private TextBox _outBox;
            private StringSendDelegate _invoker;

            private delegate void StringSendDelegate(string message);

            public TextBoxWriter(TextBox box)
            {
                _outBox = box;
                _invoker = SendString;
            }

            private void SendString(string message)
            {
                _outBox.AppendText(message);
                _outBox.ScrollToEnd();
            }


            public override Encoding Encoding { get { return Encoding.UTF8;} }

            public override void Write(string text)
            {
                _outBox.Dispatcher.Invoke(_invoker, text);
            }

            public override void WriteLine(string text)
            {
                _outBox.Dispatcher.Invoke(_invoker, text + Environment.NewLine);
            }
        }

        private void Reader_MultiSourceFrameArrived(object sender, MultiSourceFrameArrivedEventArgs e)
        {
            MultiSourceFrame msf = e.FrameReference.AcquireFrame();

            long prevTimestamp = LastTimestamp;
            long curTimestamp = DateTime.Now.Ticks;

            
            Dictionary<Readers.FrameType, Readers.Frame> cachedFrames = new Dictionary<Readers.FrameType, Readers.Frame>();
            List<TcpClient> clientsToBeDisconnected = new List<TcpClient>();

            // Will lock new connections until all previous clients are sent data
            lock (connectedClients)
            {
                // Cache all frames
                foreach (var client in connectedClients.Keys)
                {
                    foreach (var frameType in connectedClients[client])
                    {
                        if (!cachedFrames.ContainsKey(frameType))
                        {
                            KSIM.Readers.Frame frame = frameType.GetReader().Read(msf);
                            if (frame != null)
                            {
                                frame.Timestamp = curTimestamp;
                                cachedFrames[frameType] = frame;
                            }
                            else
                            {
                                // To ensure synchronization, do not send any frame if one of the subscribed ones is unavailable (null)
                                // But we still need to dispose of the frames already cached
                                goto DisposeFrames;
                            }
                        }
                    }
                }

                // We are ensured that all the subscribed frames have already been cached
                foreach (var client in connectedClients.Keys)
                {
                    // Send all subscribed frames to client together to avoid synchronization issues between subscribed streams
                    foreach(var frameType in connectedClients[client])
                    {
                        using (var ms = new MemoryStream())
                        {
                            cachedFrames[frameType].Serialize(ms);
                            byte[] dataToSend = ms.ToArray();
                            try
                            {
                                client.GetStream().Write(dataToSend, 0, dataToSend.Length);
                            }
                            catch (IOException)
                            {
                                Trace.WriteLine(String.Format("Client {0} disconnected", client.Client.RemoteEndPoint.ToString()));
                                clientsToBeDisconnected.Add(client);
                                // No need to send other frames subscribed by the client since it is already disconnected
                                break;
                            }
                        }
                    }
                }

                // If the current timestamp data has been sent to atleast one client
                // then update future timestamps to be used by Audio stream
                if (clientsToBeDisconnected.Count < connectedClients.Count)
                    LastTimestamp = prevTimestamp;

                // Remove clients that are already disconnected
                foreach (var client in clientsToBeDisconnected)
                {
                    client.Close();
                    connectedClients.Remove(client);
                }
            }
            
            DisposeFrames:
            // Dispose frames quickly otherwise Kinect will hang
            foreach (var frame in cachedFrames.Values)
                frame.Dispose();
        }

        private void Reader_AudioFrameArrived(object sender, AudioBeamFrameArrivedEventArgs e)
        {
            var audioBeamList = e.FrameReference.AcquireBeamFrames();

            var f = (AudioFrame)KSIM.Readers.FrameType.Audio.GetReader().Read(audioBeamList);
            if (f == null)
                return;

            f.Timestamp = LastTimestamp;
            
            List<TcpClient> clientsToBeDisconnected = new List<TcpClient>();

            using (var ms = new MemoryStream())
            {
                f.Serialize(ms);
                // Dispose quickly else Kinect will hang
                f.Dispose();
                // Cache
                byte[] dataToSend = ms.ToArray();
                lock(connectedAudioClients)
                {
                    foreach(var client in connectedAudioClients)
                    {
                        try
                        {
                            client.GetStream().Write(dataToSend, 0, dataToSend.Length);
                        }
                        catch (IOException)
                        {
                            Trace.WriteLine(String.Format("Client {0} disconnected", client.Client.RemoteEndPoint.ToString()));
                            clientsToBeDisconnected.Add(client);
                        }
                    }

                    // Remove clients that are already disconnected
                    foreach (var client in clientsToBeDisconnected)
                    {
                        client.Close();
                        connectedAudioClients.Remove(client);
                    }
                }
            }
        }

        private void Reader_SpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            var result = e.Result;
            var sr = (SpeechReader)KSIM.Readers.FrameType.Speech.GetReader();
            textBox.AppendText($"Phrase \"{result.Text}\" (confidence: {result.Confidence})\n");
            textBox.AppendText($"{result.Semantics["Tag"].Value},{result.Text}\n");
            textBox.ScrollToEnd();
            sr.Store(result);
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

        private bool InitializeKinect()
        {
            sensor = KinectSensor.GetDefault();
            if (sensor != null)
            {
                this.multiSourceFrameReader = sensor.OpenMultiSourceFrameReader(FrameSourceTypes.Depth | FrameSourceTypes.Color | FrameSourceTypes.Body);

                bool audioInitialized = InitializeKinectAudio();
                if (!audioInitialized && listenFromKinect)
                {
                    throw new Exception("Unable to initialize Kinect Audio, so can't listen from it");
                }

                if (!InitializeSpeechEngine(_grammarFile))
                {
                    throw new Exception("Unable to initialize Speech Engine");
                }

                
                sensor.Open();
                multiSourceFrameReader.MultiSourceFrameArrived += Reader_MultiSourceFrameArrived;
                return true;
            }
            return false;
        }

        private bool InitializeKinectAudio()
        {
            this.audioFrameReader = sensor.AudioSource.OpenReader();
            var beams = sensor.AudioSource.AudioBeams;
            if (beams != null && beams.Count > 0)
            {
                // Beam angle is in Radians theroretically between -1.58 to 1.58 (+- 90 degrees)
                // Practically, Kinect is limited to -0.87 to 0.87 (+- 90 degrees) with 5 degree increments
                // Note that setting beam mode and beam angle will only work if the
                // application window is in the foreground.
                // Furthermore, setting these values is an asynchronous operation --
                // it may take a short period of time for the beam to adjust.
                beams[0].AudioBeamMode = AudioBeamMode.Manual;
                beams[0].BeamAngle = 0.0f;

                this.audioStream = new Pcm16Stream(beams[0].OpenInputStream());
                // let the underlying stream know speech is going active
                this.audioStream.SpeechActive = true;

                audioFrameReader.FrameArrived += Reader_AudioFrameArrived;
                return true;
            }
            return false;
        }

        static Grammar LoadGrammar(string grammarPathString, bool forceCompile)
        {
            if (grammarPathString == null)
            {
                return null;
            }

            string compiledGrammarPathString;
            string grammarExtension = Path.GetExtension(grammarPathString);
            if (grammarExtension.Equals(".grxml", StringComparison.OrdinalIgnoreCase)) {
                compiledGrammarPathString = Path.ChangeExtension(grammarPathString, "cfg");
            } else if (grammarExtension.Equals(".cfg", StringComparison.OrdinalIgnoreCase)) {
                compiledGrammarPathString = grammarPathString;
            } else {
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

        private bool InitializeSpeechEngine(string grammarFileName)
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
        
        private void Window_Closed(object sender, EventArgs e)
        {
            lock (connectedClients)
            {
                foreach (var client in connectedClients.Keys)
                    client.Close();
            }
            lock(connectedAudioClients)
            {
                foreach (var client in connectedAudioClients)
                    client.Close();
            }
            // Stop listening too
            server.Stop();

            // Release Kinect resources
            if (multiSourceFrameReader != null)
            {
                multiSourceFrameReader.MultiSourceFrameArrived -= Reader_MultiSourceFrameArrived;
                multiSourceFrameReader.Dispose();
            }
            if (audioFrameReader != null)
            {
                audioFrameReader.FrameArrived -= Reader_AudioFrameArrived;
                audioFrameReader.Dispose();
            }

            if (audioStream != null)
            {
                audioStream.SpeechActive = false;
            }

            if (speechEngine != null)
            {
                speechEngine.SpeechRecognized -= Reader_SpeechRecognized;
                speechEngine.RecognizeAsyncStop();
            }

            if (sensor != null)
                sensor.Close();
        }
    }
}
