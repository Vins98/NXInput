using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace NXInput_Server
{
    public partial class Form1 : Form
    {

        private static ViGEmClient vigemClient;
        public static IXbox360Controller controller;
        private static Thread networkThread;
        private static UdpClient udpSocket;
        private static volatile bool running;
        private volatile bool rumbleConnected;
        private int gameRumble;
        private long testRumbleUntil;

        private bool _serverRunning;


        public Form1()
        {
            InitializeComponent();
            var testButton = new Button
            {
                Text = "Test Rumble",
                Location = new Point(226, 9),
                Size = new Size(92, 23)
            };
            testButton.Click += (sender, args) => TestRumble();
            Controls.Add(testButton);
            FormClosing += (sender, args) => StopServer();
        }

        private void TestRumble()
        {
            if (!rumbleConnected)
            {
                Log("Connect a rumble-capable NXInput client first.");
                return;
            }
            Interlocked.Exchange(ref testRumbleUntil, Stopwatch.GetTimestamp() + Stopwatch.Frequency / 2);
            Log("Sending a half-second rumble test.");
        }

        private void ControllerFeedback(object sender, Xbox360FeedbackReceivedEventArgs args)
        {
            Volatile.Write(ref gameRumble, args.LargeMotor | (args.SmallMotor << 8));
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            try
            {
                label2.Text = GetLocalIPv4();
            }
            catch (Exception ex)
            {
                label2.Text = "IP not found";
                richTextBox1.AppendText("Error getting IP: " + ex.Message + Environment.NewLine);
            }
        }

        private string GetLocalIPv4()
        {
            return Dns.GetHostEntry(Dns.GetHostName())
                      .AddressList
                      .First(ip => ip.AddressFamily == AddressFamily.InterNetwork)
                      .ToString();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (!_serverRunning)
                StartServer();
            else
                StopServer();
        }



        private void StartServer()
        {
            if (_serverRunning) return;

            try
            {
                button1.Enabled = false;

                udpSocket = new UdpClient(new IPEndPoint(IPAddress.Any, 8192));
                udpSocket.Client.ReceiveTimeout = 20;
                vigemClient = new ViGEmClient();
                controller = vigemClient.CreateXbox360Controller();
                controller.FeedbackReceived += ControllerFeedback;
                controller.Connect();
                Log("Virtual controller created.");

                running = true;
                rumbleConnected = false;

                networkThread = new Thread(networking);
                networkThread.Start();

                _serverRunning = true;
                button1.Text = "Stop Server";
            }
            catch (Exception ex)
            {
                Log("Error starting server: " + ex.Message);
                StopServer();
            }
            finally
            {
                button1.Enabled = true;
            }
        }

        private void StopServer()
        {
            if (!_serverRunning && udpSocket == null && vigemClient == null) return;

            try
            {
                running = false;
                rumbleConnected = false;

                udpSocket?.Close();
                networkThread?.Join(1000);
                udpSocket = null;
                networkThread = null;

                if (controller != null) controller.FeedbackReceived -= ControllerFeedback;
                Volatile.Write(ref gameRumble, 0);
                Interlocked.Exchange(ref testRumbleUntil, 0);
                controller?.Disconnect();
                vigemClient?.Dispose();
                controller = null;
                vigemClient = null;

                Log("Server stopped.");
            }
            catch (Exception ex)
            {
                Log("Error stopping server: " + ex.Message);
            }
            finally
            {
                _serverRunning = false;
                button1.Text = "Start Server";
                button1.Enabled = true;
            }
        }

        private void networking()
        {
            try
            {
                var sender = new IPEndPoint(IPAddress.Any, 0);
                IPEndPoint clientEndPoint = null;
                long lastInputAt = 0;
                long nextRumbleAt = 0;
                var rumblePacket = new byte[] { 0x4E, 0x58, 0x52, 0, 0 };

                Log("Waiting for Switch discovery...");

                while (running)
                {
                    try
                    {
                        long now = Stopwatch.GetTimestamp();
                        if (clientEndPoint != null && now - lastInputAt > Stopwatch.Frequency * 2)
                        {
                            clientEndPoint = null;
                            rumbleConnected = false;
                            ResetController();
                            Log("Switch disconnected. Waiting for discovery...");
                        }
                        if (rumbleConnected && now >= nextRumbleAt)
                        {
                            bool testing = now < Interlocked.Read(ref testRumbleUntil);
                            int intensity = testing ? 0x7FC8 : Volatile.Read(ref gameRumble);
                            rumblePacket[3] = (byte)(intensity & 0xff);
                            rumblePacket[4] = (byte)(intensity >> 8);
                            udpSocket.Send(rumblePacket, rumblePacket.Length, clientEndPoint);
                            nextRumbleAt = now + Stopwatch.Frequency / 20;
                        }
                        byte[] data = udpSocket.Receive(ref sender);
                        if (data.Length > 5 && (clientEndPoint == null || clientEndPoint.Address.Equals(sender.Address)))
                        {
                            string hello = Encoding.ASCII.GetString(data).TrimEnd('\0');
                            if (hello != "xbox_switch" && hello != "xbox_switch_rumble") continue;

                            bool supportsRumble = hello == "xbox_switch_rumble";
                            byte[] reply = Encoding.ASCII.GetBytes(supportsRumble ? "xbox_rumble\0" : "xbox\0");
                            udpSocket.Send(reply, reply.Length, sender);
                            if (!sender.Equals(clientEndPoint))
                            {
                                ResetController();
                                Log($"Connected to: {sender.Address} (rumble: {supportsRumble})");
                            }
                            clientEndPoint = sender;
                            lastInputAt = Stopwatch.GetTimestamp();
                            rumbleConnected = supportsRumble;
                            nextRumbleAt = 0;
                            continue;
                        }
                        if (data.Length < 2 || !sender.Equals(clientEndPoint)) continue;
                        lastInputAt = Stopwatch.GetTimestamp();

                        byte code = data[0];
                        byte v1 = data[1];

                        if (code <= 0x0F)
                        {
                            var btn = MapCodeToXboxButton(code);
                            if (btn != null) controller.SetButtonState(btn, v1 != 0);
                        }
                        else if (code == 0x10) // ZL
                        {
                            controller.SetSliderValue(Xbox360Slider.LeftTrigger, v1 == 0 ? (byte)0x00 : (byte)0xFF);
                        }
                        else if (code == 0x11) // ZR
                        {
                            controller.SetSliderValue(Xbox360Slider.RightTrigger, v1 == 0 ? (byte)0x00 : (byte)0xFF);
                        }
                        else if (code == 0x12 && data.Length == 5) // Left stick
                        {
                            short lx = (short)((data[1] << 8) | data[2]);
                            short ly = (short)((data[3] << 8) | data[4]);
                            controller.SetAxisValue(Xbox360Axis.LeftThumbX, lx);
                            controller.SetAxisValue(Xbox360Axis.LeftThumbY, ly);
                        }
                        else if (code == 0x13 && data.Length == 5) // Right stick
                        {
                            short rx = (short)((data[1] << 8) | data[2]);
                            short ry = (short)((data[3] << 8) | data[4]);
                            controller.SetAxisValue(Xbox360Axis.RightThumbX, rx);
                            controller.SetAxisValue(Xbox360Axis.RightThumbY, ry);
                        }
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut ||
                        ex.SocketErrorCode == SocketError.ConnectionReset) { }
                }
            }
            catch (Exception ex)
            {
                if (running) Log("Networking error: " + ex.Message);
            }
        }

        private void ResetController()
        {
            controller.ResetReport();
            controller.SubmitReport();
            Volatile.Write(ref gameRumble, 0);
            Interlocked.Exchange(ref testRumbleUntil, 0);
        }

        private static Xbox360Button MapCodeToXboxButton(byte code)
        {
            switch (code)
            {
                case 0x01: return Xbox360Button.Up;
                case 0x02: return Xbox360Button.Down;
                case 0x03: return Xbox360Button.Left;
                case 0x04: return Xbox360Button.Right;
                case 0x05: return Xbox360Button.Back;
                case 0x06: return Xbox360Button.Start;
                case 0x07: return Xbox360Button.LeftThumb;
                case 0x08: return Xbox360Button.RightThumb;
                case 0x09: return Xbox360Button.LeftShoulder;
                case 0x0A: return Xbox360Button.RightShoulder;
                case 0x0C: return Xbox360Button.B;
                case 0x0D: return Xbox360Button.A;
                case 0x0E: return Xbox360Button.Y;
                case 0x0F: return Xbox360Button.X;
                default: return null;
            };
        }

        private void SetButtonState(Xbox360Button btn, bool pressed)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => SetButtonState(btn, pressed)));
                return;
            }
            controller.SetButtonState(btn, pressed);
        }

        private void displayButtons()
        {
            while (running)
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(displayButtons));
                    Thread.Sleep(50);
                    continue;
                }
                Log("Controller active...");
                Thread.Sleep(100);
            }
        }

        private void Log(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => Log(text)));
                return;
            }
            richTextBox1.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}\n");
            richTextBox1.ScrollToCaret();
        }

    }
}
