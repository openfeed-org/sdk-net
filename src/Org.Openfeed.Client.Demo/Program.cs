using System;
using System.Linq;
using System.Threading;
using Org.Openfeed;
using Org.Openfeed.Client;

class Program {
    static void Main() {
        var listeners = new OpenfeedListeners();
        listeners.OnConnected = connection => {
            Console.WriteLine("Connected.");
            return default;
        };
        listeners.OnDisconnected = () => {
            Console.WriteLine("Disconnected.");
            return default;
        };
        listeners.OnMessageWithMetadata = (msg, definition, symbols) => {
            Console.WriteLine("------------------------------------------------------------------------");
            Console.WriteLine("Symbols: " + (symbols.Length > 0 ? symbols.Aggregate((a, b) => a + ", " + b) : ""));
            Console.WriteLine("Instrument: " + definition);
            Console.WriteLine("Message: " + msg.ToString());

            return default;
        };
        listeners.OnConnectFailed = ex => {
            Console.WriteLine(ex);
            return default;
        };
        listeners.OnCredentialsRejected = () => {
            Console.Error.WriteLine("Credentials rejected.");
            Environment.Exit(1);
            return default;
        };

        Console.Write("Username: ");
        var username = Console.ReadLine();
        
        Console.Write("Password: ");
        var password = Console.ReadLine();

        Console.Write("Symbol or /exchange (examples: MSFT, /CME): ");
        var topic = Console.ReadLine();

        string[] symbols = null, exchanges = null;

        if (topic.StartsWith('/')) {
            exchanges = new[] { topic[1..].Trim() };
        }
        else {
            symbols = new[] { topic };
        }

        var client = OpenfeedFactory.CreateClient(new Uri("ws://openfeed.aws.barchart.com/ws"), username, password, listeners);
        
        client.Subscribe(Service.RealTime, SubscriptionType.All, 1, symbols: symbols, exchanges: exchanges);

        Thread.Sleep(Timeout.Infinite);
    }
}
