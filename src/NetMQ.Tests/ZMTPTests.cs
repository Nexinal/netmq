using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NetMQ.Monitoring;
using NetMQ.Sockets;
using Xunit;

#if NET47
using ZeroMQ;
#endif

// ReSharper disable AccessToDisposedClosure
// ReSharper disable ExceptionNotDocumented

namespace NetMQ.Tests
{
    public class ZMTPTests : IClassFixture<CleanupAfterFixture>
    {
        public ZMTPTests() => NetMQConfig.Cleanup();

        [Fact]
        public void V2Test()
        {
            using (var raw = new StreamSocket())
            using (var socket = new DealerSocket())
            {
                int port = raw.BindRandomPort("tcp://*");
                socket.Connect($"tcp://localhost:{port}");

                var routingId = raw.ReceiveFrameBytes();
                var preamble = raw.ReceiveFrameBytes();
                Assert.Equal(10, preamble.Length);

                raw.SendMoreFrame(routingId).SendFrame(new byte[] {0xff,0,0,0,0,0,0,0,0,0x7f,1,5}); // Signature
                raw.SendMoreFrame(routingId).SendFrame(new byte[] {0, 0}); // Empty Identity
                raw.SendMoreFrame(routingId).SendFrame(new byte[] {0, 1, 5}); // One byte message
                
                // Receive rest of the greeting
                raw.SkipFrame(); // RoutingId
                var signature = raw.ReceiveFrameBytes();
                
                if (signature.Length == 2)
                    Assert.Equal(new byte[] {3, 5}, signature);
                else if (signature.Length == 1)
                {
                    // Receive rest of the greeting
                    raw.SkipFrame(); // RoutingId
                    signature = raw.ReceiveFrameBytes();
                    Assert.Equal(new byte[] {5}, signature);
                }
                
                // Receive the identity
                raw.SkipFrame(); // RoutingId
                var identity = raw.ReceiveFrameBytes();
                Assert.Equal(new byte[] {0, 0}, identity);
                
                // Receiving msg send by the raw
                var msg = socket.ReceiveFrameBytes();
                Assert.Equal(new byte[] {5}, msg);
                
                // Sending msg from socket to raw
                socket.SendFrame(new byte[]{6});
                raw.SkipFrame(); // RoutingId
                msg = raw.ReceiveFrameBytes();
                Assert.Equal(new byte[]{0,1,6}, msg);
            }
        }
        
        [Fact]
        public void V3Test()
        {
            using (var raw = new StreamSocket())
            using (var socket = new DealerSocket())
            {
                int port = raw.BindRandomPort("tcp://*");
                socket.Connect($"tcp://localhost:{port}");

                var routingId = raw.ReceiveFrameBytes();
                var preamble = raw.ReceiveFrameBytes();
                Assert.Equal(10, preamble.Length);

                raw.SendMoreFrame(routingId).SendFrame(
                    new byte[64]
                    {
                        0xff,0,0,0,0,0,0,0,0,0x7f,3,0, (byte)'N', (byte)'U', (byte)'L', (byte)'L',
                        0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0
                    }); // V3 Greeting
                raw.SendMoreFrame(routingId).SendFrame(
                    new byte[43]
                    {
                        4, 41,
                        5, (byte)'R', (byte)'E',(byte)'A',(byte)'D',(byte)'Y',
                        11, (byte)'S',(byte)'o',(byte)'c',(byte)'k',(byte)'e',(byte)'t',(byte)'-',(byte)'T',(byte)'y',(byte)'p',(byte)'e',
                        0,0,0,6,(byte)'D',(byte)'E',(byte)'A',(byte)'L',(byte)'E',(byte)'R',
                        8,(byte)'I',(byte)'d',(byte)'e',(byte)'n',(byte)'t',(byte)'i',(byte)'t',(byte)'y',
                        0,0,0,0
                    }); // Ready Command
                
                int read = 0;
                while (read < 64 + 43 - 10)
                {
                    raw.SkipFrame(); // RoutingId
                    var bytes = raw.ReceiveFrameBytes();
                    read += bytes.Length;
                }
                
                raw.SendMoreFrame(routingId).SendFrame(new byte[] {0, 1, 5}); // One byte message
                
                // Receiving msg send by the raw
                var msg = socket.ReceiveFrameBytes();
                Assert.Equal(new byte[] {5}, msg);
                
                // Sending msg from socket to raw
                socket.SendFrame(new byte[]{6});
                raw.SkipFrame(); // RoutingId
                msg = raw.ReceiveFrameBytes();
                Assert.Equal(new byte[]{0,1,6}, msg);
                
                // Sending ping message
                raw.SendMoreFrame(routingId).SendFrame(new byte[11] {4, 9, 4, (byte)'P', (byte)'I', (byte)'N', (byte)'G', 0, 0,(byte)'H', (byte)'I'});
                
                // Receive pong
                raw.SkipFrame(); // RoutingId
                var ping = raw.ReceiveFrameBytes();
                Assert.Equal(new byte[9] {4,7,4,(byte)'P', (byte)'O', (byte)'N', (byte)'G', (byte)'H', (byte)'I'}, ping);
            }
        }

        #if NET47
        [Fact]
        public void WithLibzmq()
        {
            using (var socket = new DealerSocket())
            using (var ctx = new ZContext())
            using (var zmq = ZSocket.Create(ctx, ZSocketType.DEALER))
            {
                zmq.Bind($"tcp://127.0.0.1:55367");
                socket.Connect("tcp://127.0.0.1:55367");

                zmq.SendBytes(Encoding.ASCII.GetBytes("Hello"), 0, 5);
                var hello = socket.ReceiveFrameString();
                Assert.Equal("Hello", hello);

                socket.SendFrame("Hello");
                var frame = zmq.ReceiveFrame();
                Assert.Equal("Hello", frame.ReadString());
            }
        }

        #endif
    }
}