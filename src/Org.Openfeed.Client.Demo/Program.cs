using System;
using System.Threading;
using System.Threading.Tasks;
using Org.Openfeed;

namespace Org.Openfeed.Client.Demo {
    class Program {
        static async Task Main(string[] args) {
            var listeners = new OpenfeedListeners();
            listeners.OnConnected = connection => {
                Console.WriteLine("Connected.");
                return default;
            };
            listeners.OnDisconnected = () => {
                Console.WriteLine("Disconnected.");
                return default;
            };
            listeners.OnMessage = msg => {
                Console.WriteLine(msg.ToString());
                return default;
            };
            listeners.OnConnectFailed = ex => {
                Console.WriteLine(ex);
                return default;
            };
            listeners.OnCredentialsRejected = () => {
                Console.WriteLine("Credentials rejected.");
                return default;
            };

            Console.WriteLine("Username:");
            var username = Console.ReadLine();
            Console.WriteLine("Password:");
            var password = Console.ReadLine();

            var client = OpenfeedFactory.CreateClient(new Uri("ws://openfeed.aws.barchart.com/ws"), username, password, listeners);
            var subId = client.Subscribe(Service.RealTime, SubscriptionType.All, 1, symbols: new[] { "MSFT" });
            await Task.Delay(Timeout.Infinite);
        }
    }
}
