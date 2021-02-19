using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using OBSWebsocketDotNet;

namespace client
{
    class Program
    {
        static void Main(string[] args)
        {
            var a = new ObsClient();
            a.Connect("ws://localhost:4444", "");
            // a.Mute("Christos A/V");
            var sr = new ServiceClient(a);
            Console.ReadLine();
        }
    }

    public class ServiceClient
    {
        private readonly HubConnection _connection;
        private readonly ObsClient _obs;

        public ServiceClient(ObsClient obs)
        {
            _obs = obs;
            _connection = new HubConnectionBuilder()
                .WithUrl("https://425bot.azurewebsites.net/twitch")
                .WithAutomaticReconnect()
                .Build();
            this.Init().Wait();
        }

        private async Task Init()
        {
            try
            {
                await _connection.StartAsync();
                Console.WriteLine("connected to signalr");
            }
            catch (System.Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            _connection.On<string>("Redeemed", message =>
            {
                var newMessage = $"{message}";
                Console.WriteLine(newMessage);
                switch (newMessage)
                {
                    case "Mute Christos for 30s":
                        {
                            Task.Run(() => _obs.Mute("Christos A/V"));
                            break;
                        }
                    case "Mute JPD for 30s":
                        {
                            Task.Run(() => _obs.Mute("Yeti Blue"));
                            break;
                        }
                    default:
                        {
                            break;
                        }
                }
            });
        }
    }

    public class ObsClient
    {
        private readonly OBSWebsocket _obs;

        public ObsClient()
        {
            _obs = new OBSWebsocket();
            _obs.Connected += (sender, args) =>
            {
                Console.WriteLine("Connected to OBS");
            };
        }

        public void Connect(string server, string pass)
        {
            _obs.Connect(server, pass);
        }

        public async Task Mute(string name)
        {
            Console.WriteLine($"{name}; is muted: {_obs.GetMute(name)}");
            _obs.SetMute(name, !_obs.GetMute(name));
            _obs.SetSourceRender($"{name} - Mute", true);
            _obs.TransitionToProgram(-1, "Cut");
            // identity noir --> smokey, old detective show
            Console.WriteLine($"{name}; is muted: {_obs.GetMute(name)}...waiting 30 seconds");
            await Task.Delay(30000);
            Console.WriteLine($"times up - unmuting {name}");
            _obs.SetSourceRender($"{name} - Mute", false);
            _obs.TransitionToProgram(-1, "Cut");
            _obs.SetMute(name, !_obs.GetMute(name));
        }
    }
}