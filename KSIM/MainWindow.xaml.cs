using KSIM.Readers;
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
        private static int PORT = 8000;

        private static bool listenFromKinect = true;
        private string _grammarFile;

        /// <summary>
        /// Reference to the Kinect sensor. Needed to Close() at the application exit.
        /// </summary>
        private static KinectSensor sensor;

        /// <summary>
        /// The server object to listen for incoming client requests
        /// </summary>
        private TcpListener server = new TcpListener(IPAddress.Any, PORT);
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
            catch(ObjectDisposedException e)
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

            var p = new OptionSet
            {
                {
                    "l=|listen=", "the microphone to use in speech module.",
                    v => listenFromKinect = v.ToLower().StartsWith("k")
                },
                {
                    "p=|port=", "port number to use to send kinect streams.",
                    v =>  PORT = v != null ? Int32.Parse(v) : 8000
                },
                {
                    "g=|grammar=", "grammar file name to use for speech (cfg or grxml).",
                    v => _grammarFile = v
                }
            };

            p.Parse(args);

            LastTimestamp = Int64.MinValue;
            bool success = InitializeKinect();
            if (success)
            {
                server.Start();
                ContinueAcceptConnections();
                InitializeComponent();
                textBox.Clear();
                Trace.Listeners.Add(new TextWriterTraceListener(new TextBoxWriter(textBox)));
                Trace.WriteLine(string.Format("App started at port {0} using {1} microphone and {2} grammar", PORT, listenFromKinect? "k" : "m", _grammarFile ?? "default"));

            }
        }

        class TextBoxWriter : TextWriter
        {

            private TextBox outputBox;

            public TextBoxWriter(TextBox box)
            {
                outputBox = box;
            }

            public override Encoding Encoding { get { return System.Text.Encoding.ASCII;} }

            public override void Write(char text)
            {
                outputBox.AppendText(text.ToString());
                outputBox.ScrollToEnd();
            }

            public override void WriteLine(char text)
            {
                Write(text);
                outputBox.AppendText("\n");
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
                    throw new InvalidOperationException("Unable to initialize Kinect Audio, so can't listen from it");    
                }
                else
                    InitializeSpeechEngine(_grammarFile);

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
            List<Grammar> grammars = null;
            if (listenFromKinect)
            {
                RecognizerInfo ri = TryGetKinectRecognizer();
                if (null != ri)
                {
                    speechEngine = new SpeechRecognitionEngine(ri.Id);
                    speechEngine.SetInputToAudioStream(audioStream, new SpeechAudioFormatInfo(EncodingFormat.Pcm, 16000, 16, 1, 32000, 2, null));

                    if (grammarFileName == null)
                    {
                        GetGrammars(ri.Culture, out grammars);
                    }
                    else
                    {
                        grammars = new List<Grammar>();
                        grammars.Add(LoadGrammar(grammarFileName, true));
                    }
                }
            }
            else
            {
                CultureInfo ci = new CultureInfo("en-us");
                if (ci != null)
                {
                    speechEngine = new SpeechRecognitionEngine(ci);
                    speechEngine.SetInputToDefaultAudioDevice();
                    if (grammarFileName == null)
                    {
                        GetGrammars(ci, out grammars);
                    }
                    else
                    {
                        grammars = new List<Grammar>();
                        grammars.Add(LoadGrammar(grammarFileName, true));
                    }
                }
            }

            if (grammars.Count > 0)
            {
                foreach (Grammar g in grammars)
                {
                    speechEngine.LoadGrammar(g);
                }
                speechEngine.RecognizeAsync(RecognizeMode.Multiple);
                speechEngine.SpeechRecognized += Reader_SpeechRecognized;
                return true;
            }
            return false;
        }

        private void GetGrammars(CultureInfo ci, out List<Grammar> grammars)
        {
            grammars = new List<Grammar>();
            var properties = new Choices();
            // Colors
            properties.Add(new SemanticResultValue("red", "RED"));
            properties.Add(new SemanticResultValue("green", "GREEN"));
            properties.Add(new SemanticResultValue("yellow", "YELLOW"));
            properties.Add(new SemanticResultValue("purple", "PURPLE"));
            properties.Add(new SemanticResultValue("black", "BLACK"));
            properties.Add(new SemanticResultValue("white", "WHITE"));
            properties.Add(new SemanticResultValue("orange", "ORANGE"));
            // Size
            properties.Add(new SemanticResultValue("big", "BIG"));
            properties.Add(new SemanticResultValue("small", "SMALL"));

        
            var refs = new Choices("one", "block");


            var propertiesGrammarBuilder = new GrammarBuilder { Culture = ci };
            propertiesGrammarBuilder.Append(new SemanticResultKey("property", properties));
            propertiesGrammarBuilder.Append(refs);

            grammars.Add(new Grammar(propertiesGrammarBuilder));

            // Locations
            var xLocationGrammarBuilder = new GrammarBuilder { Culture = ci };

            var xDirections = new Choices();
            xDirections.Add(new SemanticResultValue("left", "LEFT"));
            xDirections.Add(new SemanticResultValue("right", "RIGHT"));

            xLocationGrammarBuilder.Append("on");
            xLocationGrammarBuilder.Append("the");
            xLocationGrammarBuilder.Append(new SemanticResultKey("xDirection", xDirections));

            grammars.Add(new Grammar(xLocationGrammarBuilder));

            var yLocationGrammarBuilder = new GrammarBuilder { Culture = ci };

            var yDirections = new Choices();
            yDirections.Add(new SemanticResultValue("front", "FRONT"));
            yDirections.Add(new SemanticResultValue("back", "BACK"));

            yLocationGrammarBuilder.Append("at");
            yLocationGrammarBuilder.Append("the");
            yLocationGrammarBuilder.Append(new SemanticResultKey("yDirection", yDirections));


            grammars.Add(new Grammar(yLocationGrammarBuilder));

            // Answers yes/no
            var answersGrammarBuilder = new GrammarBuilder { Culture = ci };
            var answers = new Choices();
            answers.Add(new SemanticResultValue("yes", "YES"));
            answers.Add(new SemanticResultValue("yeah", "YES"));
            answers.Add(new SemanticResultValue("please", "YES"));
            answers.Add(new SemanticResultValue("no", "NO"));
            answers.Add(new SemanticResultValue("nothing", "NOTHING"));
            answers.Add(new SemanticResultValue("never mind", "NEVERMIND"));

            answersGrammarBuilder.Append(new SemanticResultKey("answer", answers));

            grammars.Add(new Grammar(answersGrammarBuilder));

            // Actions 
            var actionsGrammarBuilder = new GrammarBuilder { Culture = ci };
            var actions = new Choices();
            actions.Add(new SemanticResultValue("grab", "GRAB"));
            actions.Add(new SemanticResultValue("lift", "LIFT"));
            actions.Add(new SemanticResultValue("push", "PUSH"));
            actions.Add(new SemanticResultValue("put", "PUT"));

            actionsGrammarBuilder.Append(new SemanticResultKey("action", actions));

            grammars.Add(new Grammar(actionsGrammarBuilder));


            // Demonstratives
            var demonstrativesGrammarBuilder = new GrammarBuilder { Culture = ci };
            var demonstratives = new Choices();
            demonstratives.Add(new SemanticResultValue("this", "THIS"));
            demonstratives.Add(new SemanticResultValue("that", "THAT"));

            demonstrativesGrammarBuilder.Append(new SemanticResultKey("demonstrative", demonstratives));
            demonstrativesGrammarBuilder.Append(refs);

            grammars.Add(new Grammar(demonstrativesGrammarBuilder));

            // Others
            var othersGrammarBuilder = new GrammarBuilder { Culture = ci };
            var others = new Choices();
            others.Add(new SemanticResultValue("there", "THERE"));
            others.Add(new SemanticResultValue("what", "WHAT"));
            others.Add(new SemanticResultValue("learn", "LEARN"));

            othersGrammarBuilder.Append(new SemanticResultKey("other", others));

            grammars.Add(new Grammar(othersGrammarBuilder));
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
