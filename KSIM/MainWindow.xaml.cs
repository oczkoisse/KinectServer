using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Globalization;
using System.Text;
using System.Windows.Controls;
using System.Collections.Concurrent;
using System.Net;
using System.Configuration;
using Microsoft.Speech.AudioFormat;
using Microsoft.Speech.Recognition;
using Microsoft.Speech.Recognition.SrgsGrammar;
using Mono.Options;

using KSIM.Frames;
using KSIM.Kinect;
using SimpleNetworking;

namespace KSIM
{
    /// <summary>
    /// Class holding the interaction logic for MainWindow.xaml.
    /// In addition, this class also handles new incoming client stream requests and
    /// interfaces with the underlying Kinect hardware to facilitate these stream requests.
    /// </summary>
    public partial class MainWindow : Window
    {
        private enum MessageType
        {
            RECOGNIZER_REG = 1,
            VOXSIM_REG,
            VOXSIM_PASS
        }

        private readonly Dictionary<FrameType, ConcurrentQueue<byte[]>> affixes = new Dictionary<FrameType, ConcurrentQueue<byte[]>>();

        private readonly ConcurrentQueue<SpeechFrame> listSpeechFrame = new ConcurrentQueue<SpeechFrame>();

        private readonly Queue<FaceFrameArrivedEventArgs> faceFrameArrivedEvents = new Queue<FaceFrameArrivedEventArgs>();

        /// <summary>
        /// The port at which the application listens for incoming stream requests for Kinect clients
        /// </summary>
        private static int port = 8000;

        private static bool listenFromKinect = false;

		private static double engageMin = 1.5;

		private static double engageMax = 5;

        private string _grammarFile = "defaultGrammar.grxml";

        private KinectSensor sensor;

        /// <summary>
        /// The server object to listen for incoming client requests
        /// </summary>
        private Server server;

        /// <summary>
        /// A dictionary for holding stream types subscribed by each client. The subscribed streams should not contain Audio or Speech
        /// </summary>
        private Dictionary<Connection, List<FrameType>> connectedClients = new Dictionary<Connection, List<FrameType>>();

        /// <summary>
        /// A dictionary for holding clients subscribed to Audio stream. These clients cannot be subscribed to any other stream.
        /// </summary>
        private List<Connection> connectedAudioClients = new List<Connection>();

        private List<Connection> connectedVoxSimClients = new List<Connection>();
        
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
        
        
        /// <summary>
        /// The constructor for MainWindow class.
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

			_grammarFile = Path.Combine(System.AppContext.BaseDirectory, _grammarFile);

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

            InitializeKinect();

            server = new Server(IPAddress.Any, port);
            server.Connected += OnConnected;
            server.Start();
            InitializeComponent();
            textBox.Clear();

			engageMin = double.Parse(ConfigurationManager.AppSettings["engage_distance_min"]);
			engageMax = double.Parse(ConfigurationManager.AppSettings["engage_distance_max"]);

			textBox.AppendText(string.Format("Engage distance min {0} and max {1}\n", engageMin, engageMax));

			textBox.AppendText(string.Format("App started at port {0} using {1} microphone and {2} grammar",
                port, listenFromKinect ? "kinect" : "normal", _grammarFile));
            foreach (FrameType ft in (FrameType[])Enum.GetValues(typeof(FrameType)))
            {
                affixes.Add(ft, new ConcurrentQueue<byte[]>());
            }
        }

        private void OnConnected(object sender, ConnectedEventArgs e)
        {
            Connection conn = e.GetConnection();
            if (e.OperationSucceeded)
            {
                conn.Received += OnReceived;
                conn.Sent += OnSent;
            }
            else
                RemoveConnection(conn);
        }

        private void OnSent(object sender, SentEventArgs e)
        {
            if (!e.OperationSucceeded)
            {
                Connection conn = e.GetConnection();
                RemoveConnection(conn);
            }
        }

        private void RemoveConnection(Connection conn)
        {
            lock (connectedAudioClients)
            {
                if (connectedAudioClients.Remove(conn))
                {
                    conn.Close();
                    return;
                }
            }
            lock (connectedClients)
            {
                if (connectedClients.Remove(conn))
                {
                    conn.Close();
                    return;
                }
            }
            lock (connectedVoxSimClients)
            {
                if (connectedVoxSimClients.Remove(conn))
                {
                    conn.Close();
                    return;
                }
            }
        }

        private void OnReceived(object sender, ReceivedEventArgs e)
        {
            Connection conn = e.GetConnection();
            if (e.OperationSucceeded)
            {
                using (MemoryStream ms = new MemoryStream(e.Data))
                {
                    using (BinaryReader br = new BinaryReader(ms))
                    {
                        // Length isn't needed actually
                        // but need to read to advance the stream
                        int length = br.ReadInt32();
                        if (length >= 1)
                        {
                            byte mt = br.ReadByte();
                            MessageType msgType = (MessageType) mt;
                            switch (msgType)
                            {
                                case MessageType.RECOGNIZER_REG:
                                    HandleRecognizerRegistration(conn, br);
                                    break;
                                case MessageType.VOXSIM_REG:
                                    HandleVoxSimRegistration(conn);
                                    break;
                                case MessageType.VOXSIM_PASS:
                                    HandleVoxSimPass(conn, br);
                                    break;
                            }
                        }
                    }
                }
            }
            else
            {
                RemoveConnection(conn);   
            }
        }

        private void HandleVoxSimPass(Connection conn, BinaryReader br)
        {
            try
            {
                byte total = br.ReadByte();
                lock(affixes)
                {
                    for (byte i = 0; i < total; i++)
                    {
                        // Get the frame types to which the content should be affixed
                        List<FrameType> activeFrames = GetActiveFrames(br.ReadInt32());
                        // Get the actual length prefixed content that should be affixed
                        int contentLength = br.ReadInt32();
                        byte[] content = br.ReadBytes(contentLength);

                        foreach (FrameType ft in activeFrames)
                        {
                            affixes[ft].Enqueue(content);
                        }
                    }
                }
            }
            catch (EndOfStreamException)
            {
                RemoveConnection(conn);
            }
            catch (IOException)
            {
                RemoveConnection(conn);
            }
        }

        private void HandleVoxSimRegistration(Connection conn)
        {
            lock(connectedVoxSimClients)
            {
                connectedVoxSimClients.Add(conn);
            }
        }

        private List<FrameType> GetActiveFrames(int requestedFrames)
        {
            FrameType frameTypeFlags = (FrameType) requestedFrames;
            
            // Parse this integer for requested frames
            List<FrameType> activeFrames = new List<FrameType>();

            // Determine which stream bits are active
            foreach (FrameType ft in Enum.GetValues(typeof(FrameType)).Cast<FrameType>())
            {
                if (frameTypeFlags.HasFlag(ft))
                    activeFrames.Add(ft);
            }

            return activeFrames;
        }

        private void HandleRecognizerRegistration(Connection conn, BinaryReader br)
        {
            int requestedFrames = -1;
            try
            {
                requestedFrames = br.ReadInt32();
            }
            catch (EndOfStreamException ex)
            {
                conn.Close();
            }
            catch(IOException ex)
            {
                conn.Close();
            }

            if (requestedFrames != -1)
            {
                List<FrameType> activeFrames = GetActiveFrames(requestedFrames);

                if (activeFrames.Count >= 1)
                {
                    // If the request does not involve Audio
                    if (!activeFrames.Contains(FrameType.Audio))
                    {
                        // Add the client to list of connected clients other than audio clients
                        lock (connectedClients)
                        {
                            connectedClients.Add(conn, activeFrames);
                        }
                    }
                    // If the request contains only 1 stream type which is Audio
                    else if (activeFrames.Count == 1 && activeFrames[0] == FrameType.Audio)
                    {
                        lock (connectedAudioClients)
                        {
                            connectedAudioClients.Add(conn);
                        }
                    }
                    else
                    {
                        // Reject as an invalid stream as the stream contains a combination of Audio/Speech with other stream types
                        conn.Close();
                    }
                }
                else
                {
                    // Reject as no valid stream was identified
                    conn.Close();
                }
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

        private void Reader_SpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            var result = e.Result;
            
            textBox.AppendText($"Phrase \"{result.Text}\" (confidence: {result.Confidence})\n");
            textBox.AppendText($"{result.Semantics["Tag"].Value},{result.Text}\n");
			if (result.Confidence < Double.Parse(ConfigurationManager.AppSettings["speech_confidence_min"]))
			{
				textBox.AppendText($"REJECTED\n");
				return;
			}
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

        private void InitializeKinect()
        {
			sensor = new KinectSensor(KinectSensor.FrameType.Audio | KinectSensor.FrameType.Body
                | KinectSensor.FrameType.Color | KinectSensor.FrameType.Depth | KinectSensor.FrameType.Face);
            sensor.MultiSourceFrameArrived += OnMultiSourceFrameArrived;

            sensor.FaceFrameArrived += OnFaceFrameArrived;

            if (sensor.AudioBeam != null)
            {
                sensor.AudioBeam.AudioBeamMode = Microsoft.Kinect.AudioBeamMode.Manual;
                // Beam angle is in Radians theroretically between -1.58 to 1.58 (+- 90 degrees)
                // Practically, Kinect is limited to -0.87 to 0.87 (+- 90 degrees) with 5 degree increments
                // Note that setting beam mode and beam angle will only work if the
                // application window is in the foreground.
                // Furthermore, setting these values is an asynchronous operation --
                // it may take a short period of time for the beam to adjust.
                sensor.AudioBeam.BeamAngle = 0.0f;

                audioStream = new Pcm16Stream(sensor.AudioBeam.OpenInputStream());
                // let the underlying stream know speech is going active
                audioStream.SpeechActive = true;

                sensor.AudioBeamFrameArrived += OnAudioBeamFrameArrived;
            }
            else if (listenFromKinect)
                throw new Exception("Unable to initialize Kinect Audio, so can't listen from it");
            
            if (!InitializeSpeechEngine(_grammarFile))
                throw new Exception("Unable to initialize Speech Engine");
        }

        private void OnFaceFrameArrived(object sender, FaceFrameArrivedEventArgs e)
        {
            if (e != null)
            {
                lock (faceFrameArrivedEvents)
                {
                    faceFrameArrivedEvents.Enqueue(e);
                }
            }
        }

        private void OnAudioBeamFrameArrived(object sender, AudioBeamFrameArrivedEventArgs e)
        {
            var audioBeamList = e.AudioBeamFrameList;
            
            if (audioBeamList != null)
            {
                var f = new AudioFrame(audioBeamList);
                f.Timestamp = e.Timestamp;
                using (var ms = new MemoryStream())
                {
                    f.Serialize(ms);
                    // Cache
                    byte[] dataToSend = ms.ToArray();
                    lock (connectedAudioClients)
                    {
                        foreach (var client in connectedAudioClients)
                        {
                            client.Write(dataToSend);
                        }
                    }
                }
            }
        }

        private void OnMultiSourceFrameArrived(object sender, MultiSourceFrameArrivedEventArgs e)
        {
            if (e.BodyFrame == null)
                return;

            Dictionary<Frames.FrameType, Frames.Frame> cachedFrames = new Dictionary<Frames.FrameType, Frames.Frame>();
            cachedFrames[Frames.FrameType.ClosestBody] = new ClosestBodyFrame(e.BodyFrame, engageMin, engageMax);
            cachedFrames[Frames.FrameType.ClosestBody].Timestamp = e.Timestamp;

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
                            Frames.Frame frame = null;
                            switch(frameType)
                            {
                                case Frames.FrameType.Color:
                                    frame = e.ColorFrame != null ? new ColorFrame(e.ColorFrame) : null;
                                    break;
                                case Frames.FrameType.Depth:
                                    frame = e.DepthFrame != null ? new DepthFrame(e.DepthFrame) : null;
                                    break;
                                case Frames.FrameType.HeadColor:                                                                                                            
                                    frame = e.ColorFrame != null ? 
                                        new HeadColorFrame(e.ColorFrame, cachedFrames[Frames.FrameType.ClosestBody] as ClosestBodyFrame) : null;
                                    break;
                                case Frames.FrameType.HeadDepth:
                                    frame = e.DepthFrame != null ? 
                                        new HeadDepthFrame(e.DepthFrame, cachedFrames[Frames.FrameType.ClosestBody] as ClosestBodyFrame) : null;
                                    break;
                                case Frames.FrameType.LHDepth:
                                    frame = e.DepthFrame != null ?
                                        new LHDepthFrame(e.DepthFrame, cachedFrames[Frames.FrameType.ClosestBody] as ClosestBodyFrame) : null;
                                    break;
                                case Frames.FrameType.RHDepth:
                                    frame = e.DepthFrame != null ?
                                        new RHDepthFrame(e.DepthFrame, cachedFrames[Frames.FrameType.ClosestBody] as ClosestBodyFrame) : null;
                                    break;
                                case Frames.FrameType.Speech:
                                    SpeechFrame speechFrame = new SpeechFrame();
                                    while (listSpeechFrame.TryDequeue(out SpeechFrame enqueuedSpeechFrame))
                                        speechFrame += enqueuedSpeechFrame;
                                    frame = speechFrame;
                                    break;
                                case Frames.FrameType.ClosestFace:
                                    lock (faceFrameArrivedEvents)
                                    {
                                        while (faceFrameArrivedEvents.Count > 0)
                                        {
                                            var ev = faceFrameArrivedEvents.Dequeue();
                                            if (ev.FaceFrameResult != null)
                                            {
                                                ClosestFaceFrame closestFaceFrame =
                                                    new Frames.ClosestFaceFrame(ev.FaceFrameResult,
                                                    cachedFrames[Frames.FrameType.ClosestBody] as ClosestBodyFrame);

                                                if (closestFaceFrame.FaceFound)
                                                    frame = closestFaceFrame;
                                            }
                                        }
                                    }
                                    if (frame == null)
                                        frame = new ClosestFaceFrame();
                                    
                                    break;
                                default:
                                    break;
                            }

                            if (frame != null)
                            {
                                frame.Timestamp = e.Timestamp;
                                cachedFrames[frameType] = frame;
                            }
                            else if (frameType != Frames.FrameType.Audio && frameType != Frames.FrameType.ClosestFace)
                            {
                                // To ensure synchronization, do not send any frame if one of the subscribed ones is unavailable (null)
                                return;
                            }
                        }
                    }
                }

                lock (affixes)
                {
                    foreach (Frames.Frame f in cachedFrames.Values)
                    {
                        if (affixes[f.Type].TryDequeue(out byte[] affix))
                        {
                            f.Affix = affix;
                        }
                    }
                }

                // We are ensured that all the subscribed frames have already been cached
                foreach (var client in connectedClients.Keys.ToList())
                {
                    // Send all subscribed frames to client together to avoid synchronization issues between subscribed streams
                    foreach (var frameType in connectedClients[client])
                    {
                        using (var ms = new MemoryStream())
                        {
                            cachedFrames[frameType].Serialize(ms);
                            byte[] dataToSend = ms.ToArray();
                            client.Write(dataToSend);
                        }
                    }
                }
            }
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
