using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace RobloxMultiByJayy
{
    internal sealed class DiscordRpcManager : IDisposable
    {
        private const int Handshake = 0, Message = 1, Close = 2, Ping = 3, Pong = 4;
        private readonly string clientId;
        private readonly string pipePrefix;
        private readonly int processId;
        private readonly Action<string, bool> onStatus;
        private readonly object gate = new object();
        private readonly Thread worker;
        private string activity;
        private volatile bool stopping;
        private bool activityMayBeVisible;
        private string lastStatus;

        internal DiscordRpcManager(string clientId, Action<string, bool> onStatus, string pipePrefix = "discord-ipc-")
        {
            this.clientId = clientId;
            this.onStatus = onStatus;
            this.pipePrefix = pipePrefix;
            using (var process = Process.GetCurrentProcess()) processId = process.Id;
            worker = new Thread(Run) { IsBackground = true, Name = "DiscordRpc" };
            worker.Start();
        }

        internal void SetActivity(string value)
        {
            lock (gate)
            {
                if (stopping || activity == value) return;
                activity = value;
                Monitor.Pulse(gate);
            }
        }

        private void Run()
        {
            while (!stopping)
            {
                lock (gate)
                {
                    while (!stopping && activity == null) Monitor.Wait(gate);
                    if (stopping) return;
                }

                try
                {
                    using (var connection = Connect())
                    {
                        if (connection == null)
                        {
                            if (!stopping) Report("Aguardando o Discord para desktop.", false);
                        }
                        else
                        {
                            string acknowledged = null;
                            while (true)
                            {
                                string desired;
                                lock (gate) desired = stopping ? null : activity;
                                if (desired == null)
                                {
                                    if (activityMayBeVisible)
                                    {
                                        SendActivity(connection, null, 650);
                                        activityMayBeVisible = false;
                                    }
                                    break;
                                }
                                if (acknowledged != desired)
                                {
                                    // Discord can display a frame before its acknowledgement arrives.
                                    activityMayBeVisible = true;
                                    if (!SendActivity(connection, desired, 2200)) continue;
                                    acknowledged = desired;
                                    if (!stopping) Report("Atividade aceita pelo Discord.", true);
                                }
                                Receive(connection, 220);
                            }
                        }
                    }
                }
                catch (RpcRejected error)
                {
                    if (!stopping) Report("O Discord recusou a atividade" + error.CodeHint + ".", false);
                }
                catch (TimeoutException)
                {
                    if (!stopping) Report("O Discord não respondeu. Tentando novamente.", false);
                }
                catch (IOException)
                {
                    if (!stopping) Report("Conexão com o Discord interrompida. Tentando novamente.", false);
                }
                catch (UnauthorizedAccessException)
                {
                    if (!stopping) Report("O Windows bloqueou o acesso ao Discord.", false);
                }
                catch (ArgumentException)
                {
                    if (!stopping) Report("A resposta do Discord não pôde ser lida.", false);
                }
                catch (InvalidOperationException)
                {
                    if (!stopping) Report("A resposta do Discord não pôde ser lida.", false);
                }

                lock (gate)
                {
                    if (!stopping && activity != null) Monitor.Wait(gate, 3000);
                }
            }
        }

        private Connection Connect()
        {
            for (int index = 0; index < 10 && !stopping; index++)
            {
                var pipe = new NamedPipeClientStream(".", pipePrefix + index, PipeDirection.InOut, PipeOptions.Asynchronous);
                try { pipe.Connect(130); }
                catch (TimeoutException) { pipe.Dispose(); continue; }
                catch { pipe.Dispose(); throw; }

                var connection = new Connection(pipe);
                try
                {
                    string hello = connection.Json.Serialize(new { v = 1, client_id = clientId });
                    connection.Write(Handshake, Encoding.UTF8.GetBytes(hello));
                    WaitForReply(connection, null, 1600);
                    return connection;
                }
                catch
                {
                    connection.Dispose();
                    throw;
                }
            }
            return null;
        }

        private bool SendActivity(Connection connection, string value, int timeout)
        {
            string nonce = Guid.NewGuid().ToString("N");
            string command = connection.Json.Serialize(new
            {
                cmd = "SET_ACTIVITY",
                args = new { pid = processId, activity = value == null ? null : connection.Json.DeserializeObject(value) },
                nonce = nonce
            });
            connection.Write(Message, Encoding.UTF8.GetBytes(command));
            return WaitForReply(connection, nonce, timeout, value != null);
        }

        private bool WaitForReply(Connection connection, string nonce, int timeout, bool interruptOnStop = false)
        {
            var timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < timeout)
            {
                int remaining = timeout - (int)timer.ElapsedMilliseconds;
                if (interruptOnStop && stopping) return false;
                int wait = interruptOnStop ? Math.Min(100, remaining) : remaining;
                var reply = Receive(connection, Math.Max(1, wait));
                if (reply == null) continue;
                string eventName = Text(reply, "evt");
                bool relevant = nonce == null || nonce == Text(reply, "nonce");
                if (relevant && eventName == "ERROR") throw new RpcRejected(reply);
                if (nonce == null && eventName == "READY") return true;
                if (nonce != null && relevant && Text(reply, "cmd") == "SET_ACTIVITY") return true;
            }
            throw new TimeoutException();
        }

        private static Dictionary<string, object> Receive(Connection connection, int timeout)
        {
            Frame frame = connection.Read(timeout);
            if (frame == null) return null;
            if (frame.Opcode == Ping)
            {
                connection.Write(Pong, frame.Body);
                return null;
            }
            if (frame.Opcode != Message && frame.Opcode != Close) return null;
            var data = connection.Json.DeserializeObject(Encoding.UTF8.GetString(frame.Body)) as Dictionary<string, object>;
            if (frame.Opcode == Close) throw new RpcRejected(data);
            return data;
        }

        private static string Text(Dictionary<string, object> data, string key)
        {
            object value;
            return data != null && data.TryGetValue(key, out value) ? value as string : null;
        }

        private void Report(string message, bool accepted)
        {
            if (message == lastStatus) return;
            lastStatus = message;
            if (onStatus != null) onStatus(message, accepted);
        }

        internal bool Stop()
        {
            lock (gate)
            {
                stopping = true;
                Monitor.Pulse(gate);
            }
            return Thread.CurrentThread != worker && worker.Join(2800) && !activityMayBeVisible;
        }

        public void Dispose() { Stop(); }

        private sealed class RpcRejected : IOException
        {
            internal readonly string CodeHint;

            internal RpcRejected(Dictionary<string, object> reply)
            {
                object data, code;
                if (reply != null && reply.TryGetValue("data", out data) && data is Dictionary<string, object>)
                    reply = (Dictionary<string, object>)data;
                CodeHint = reply != null && reply.TryGetValue("code", out code) && code is int
                    ? " (código " + code + ")" : "";
            }
        }

        private sealed class Frame
        {
            internal int Opcode;
            internal byte[] Body;
        }

        private sealed class Connection : IDisposable
        {
            private readonly NamedPipeClientStream pipe;
            private Task<Frame> incoming;
            internal readonly JavaScriptSerializer Json = new JavaScriptSerializer();

            internal Connection(NamedPipeClientStream pipe) { this.pipe = pipe; }

            internal Frame Read(int timeout)
            {
                if (incoming == null) incoming = ReadFrame();
                if (Task.WaitAny(new Task[] { incoming }, timeout) < 0) return null;
                var result = incoming.GetAwaiter().GetResult();
                incoming = null;
                return result;
            }

            private async Task<Frame> ReadFrame()
            {
                var header = await ReadBytes(8).ConfigureAwait(false);
                int count = BitConverter.ToInt32(header, 4);
                if (count < 0 || count > 1024 * 1024) throw new IOException("Invalid frame length.");
                return new Frame { Opcode = BitConverter.ToInt32(header, 0), Body = await ReadBytes(count).ConfigureAwait(false) };
            }

            private async Task<byte[]> ReadBytes(int count)
            {
                var buffer = new byte[count];
                int offset = 0;
                while (offset < count)
                {
                    int read = await pipe.ReadAsync(buffer, offset, count - offset).ConfigureAwait(false);
                    if (read == 0) throw new EndOfStreamException();
                    offset += read;
                }
                return buffer;
            }

            internal void Write(int opcode, byte[] body)
            {
                var frame = new byte[8 + body.Length];
                Buffer.BlockCopy(BitConverter.GetBytes(opcode), 0, frame, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(body.Length), 0, frame, 4, 4);
                Buffer.BlockCopy(body, 0, frame, 8, body.Length);
                var write = pipe.WriteAsync(frame, 0, frame.Length);
                if (Task.WaitAny(new Task[] { write }, 900) < 0)
                {
                    ObserveFault(write);
                    throw new TimeoutException();
                }
                write.GetAwaiter().GetResult();
            }

            private static void ObserveFault(Task task)
            {
                task.ContinueWith(failed => failed.Exception.Handle(_ => true),
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            }

            public void Dispose()
            {
                if (incoming != null) ObserveFault(incoming);
                pipe.Dispose();
            }
        }
    }
}
