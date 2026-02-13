using SekiroAPClient;

Console.WriteLine("Sekiro Archipelago C# client (pipe test)");
Console.WriteLine("Waiting for DLL connection on \\\\.\\pipe\\SekiroAP ...");

var pipeServer = new PipeServer("SekiroAP");
pipeServer.MessageReceived += OnMessageReceived;
pipeServer.Start();

Console.WriteLine("Press 'g' to send test grant_item, 'q' to quit.");

var isDebug = false;
var interruptItems = true;

// Примитивный ввод с клавиатуры
while (true)
{
    var key = Console.ReadKey(true);

    
    if (key.Key == ConsoleKey.G)
    {
        // Тестовая команда: выдать предмет 3500 x1
        string json = "{ \"type\":\"grant_item\", \"goods_id\":3500, \"quantity\":1 }";
        Console.WriteLine("[Client] Sending: " + json);
        pipeServer.SendJson(json);
    }
    if (key.Key == ConsoleKey.M)
    {
        // Тестовая команда: выдать предмет 3500 x1
        string json = "{ \"type\":\"grant_item\", \"goods_id\":3700, \"quantity\":1 }";
        Console.WriteLine("[Client] Sending: " + json);
        pipeServer.SendJson(json);
    }
    if (key.Key == ConsoleKey.P)
    {
        // Тестовая команда: выдать предмет 3500 x1
        string json = "{ \"type\":\"grant_item\", \"event_id\":51100020, \"goods_id\":3020, \"quantity\":2 }";
        Console.WriteLine("[Client] Sending: " + json);
        pipeServer.SendJson(json);
    }
    if (key.Key == ConsoleKey.L)
    {
        // Тестовая команда: выдать предмет 3500 x1
        string json = "{ \"type\":\"replay_last_popup\"}";
        Console.WriteLine("[Client] Sending: " + json);
        pipeServer.SendJson(json);
    }
    if (key.Key == ConsoleKey.NumPad1)
    {
        // Тестовая команда: выдать предмет 3500 x1
        string json = "{ \"type\":\"show_hint_by_id\", \"headerId\" : 70000, \"textId\" : 71000 }";
        Console.WriteLine("[Client] Sending: " + json);
        pipeServer.SendJson(json);
    }
    if (key.Key == ConsoleKey.NumPad2)
    {
        // Тестовая команда: выдать предмет 3500 x1
        string json = "{ \"type\":\"show_small_hint_by_id\", \"msgId\" : 15100911 }";
        Console.WriteLine("[Client] Sending: " + json);
        pipeServer.SendJson(json);
    }
    if (key.Key == ConsoleKey.NumPad3)
    {
        // Тестовая команда: выдать предмет 3500 x1
        string json = "{ \"type\":\"show_small_hint\", \"text\" : \"Hello World\"}";
        Console.WriteLine("[Client] Sending: " + json);
        pipeServer.SendJson(json);
    }
    if (key.Key == ConsoleKey.NumPad4)
    {
        // Тестовая команда: выдать предмет 3500 x1
        string json = "{ \"type\":\"show_hint\", \"header\" : \"Header\", \"text\" : \"Hello world 1\"}";
        Console.WriteLine("[Client] Sending: " + json);
        pipeServer.SendJson(json);
    }
    if (key.Key == ConsoleKey.NumPad5)
    {
        // Тестовая команда: выдать предмет 3500 x1
        string json = "{ \"type\":\"show_hint\", \"header\" : \"Header 2\", \"text\" : \"Lorem Ipsum is simply dummy text of the printing and typesetting industry. Lorem Ipsum has been the industry's standard dummy text ever since the 1500s, when an unknown\"}";
        Console.WriteLine("[Client] Sending: " + json);
        pipeServer.SendJson(json);
    }
    if (key.Key == ConsoleKey.Q)
    {
        interruptItems = !interruptItems;
        string json = "{ \"type\":\"interrupt_acquiring\", \"value\" : " + interruptItems +"}";
        Console.WriteLine("[Client] Sending: " + json);
        pipeServer.SendJson(json);
    }   
    if (key.Key == ConsoleKey.D)
    {
        isDebug = !isDebug;
        string json = "{ \"type\":\"debug_state\", \"value\" : "+ isDebug +"}";
        Console.WriteLine("[Client] Sending: " + json);
        pipeServer.SendJson(json);
    }
    if (key.Key == ConsoleKey.F)
    {
        isDebug = !isDebug;
        string json = "{ \"type\":\"register_foreign_pickup_lots\", \"lots\" : [123,123,123,123,123,5432,1123421,111000,432,1234,1334010,1120000]}";
        Console.WriteLine("[Client] Sending: " + json);
        pipeServer.SendJson(json);
    }

}

Console.WriteLine("Stopping PipeServer...");
pipeServer.Stop();
Console.WriteLine("Bye.");


static void OnMessageReceived(string json)
{
    // Тут будут прилетать сообщения вида item_picked, hello и т.п.
    Console.WriteLine("[DLL] " + json);

}