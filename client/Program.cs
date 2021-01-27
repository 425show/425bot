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
            var sr = new ServiceClient();
            Console.ReadLine();
        }
    }

    public class ServiceClient
    {
        private readonly HubConnection _connection;
        public ServiceClient()
        {
            _connection = new HubConnectionBuilder()
                .WithUrl("https://425bot.ngrok.io/twitch")
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
            //_obs.Connect(server, pass);
        }

        public void Mute(string name)
        {
            Console.WriteLine($"is muted: {_obs.GetMute(name)}");
            _obs.SetMute(name, !_obs.GetMute(name));
            // identity noir --> smokey, old detective show
            Console.WriteLine($"is muted: {_obs.GetMute(name)}");
        }
    }
}