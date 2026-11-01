using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using MongoDB.Driver;

namespace PetSitter.Tests
{
    public class MongoTestServer : IDisposable
    {
        private readonly Process _process;
        private readonly string _dbPath;
        private readonly string _logPath;
        public int Port { get; }
        public string ConnectionString => $"mongodb://127.0.0.1:{Port}";
        public IMongoClient Client { get; }

        public MongoTestServer()
        {
            Port = GetFreePort();
            _dbPath = Path.Combine(Path.GetTempPath(), "petsitter_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dbPath);
            _logPath = Path.Combine(_dbPath, "mongod.log");

            var mongodPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                @".nuget\packages\mongo2go\3.1.3\tools\mongodb-windows-4.4.4-database-tools-100.3.1\bin\mongod.exe"
            );

            if (!File.Exists(mongodPath))
            {
                throw new FileNotFoundException("Cannot find mongod.exe at " + mongodPath);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = mongodPath,
                Arguments = $"--dbpath \"{_dbPath}\" --port {Port} --bind_ip 127.0.0.1 --nojournal --quiet --logpath \"{_logPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };

            _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start mongod process.");

            Client = new MongoClient(ConnectionString);

            // Chờ mongod sẵn sàng
            var ready = false;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 15000)
            {
                try
                {
                    Client.GetDatabase("admin").RunCommand<MongoDB.Bson.BsonDocument>(new MongoDB.Bson.BsonDocument("ping", 1));
                    ready = true;
                    break;
                }
                catch
                {
                    System.Threading.Thread.Sleep(150);
                }
            }

            if (!ready)
            {
                Dispose();
                throw new TimeoutException("Timed out waiting for local mongod server to start.");
            }
        }

        private static int GetFreePort()
        {
            using var tcp = new TcpListener(IPAddress.Loopback, 0);
            tcp.Start();
            var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
            tcp.Stop();
            return port;
        }

        public void Dispose()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill();
                    _process.WaitForExit(3000);
                }
                _process.Dispose();
            }
            catch { }

            try
            {
                if (Directory.Exists(_dbPath))
                {
                    Directory.Delete(_dbPath, true);
                }
            }
            catch { }
        }
    }
}
