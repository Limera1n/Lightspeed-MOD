using AssettoServer.Server;
using AssettoServer.Network.Tcp;
using AssettoServer.Network.Packets;
using Serilog;
using System.Text.Json;
using System.Text.RegularExpressions;
using LightspeedPlugin.Managers;
using LightspeedPlugin.Session;
using AssettoServer.Server.Configuration;

namespace LightspeedPlugin;

public class LightspeedPlugin
{
    public const string ENTRY_CAR_DATA_DIR = "entry_car_data";
    public const string LIGHTSPEED_PLAYERS_DIR = "lightspeed_players";
    public const string SUMMARY_FILE = "summary.json";
    public const string NO_NAME = "No Name";
    public const string UNKNOWN = "Unknown";

    public const string SERVER_NAME_CUT_UP = "Cut Up";
    public const string SERVER_NAME_DRIFT = "Drift";

    public class entryCarData
    {
        public double TotalKmDriven { get; set; } = 0;
        public int BestTopSpeed { get; set; } = 0;
        public string MostCommonDriver { get; set; } = NO_NAME;
    }

    public class clientEntryCarData
    {
        public string Username { get; set; } = NO_NAME;
        public double KmDriven { get; set; } = 0;
        public double TimeSpent { get; set; } = 0;
        public int TopSpeed { get; set; } = 0;
    }

    public string serverTag = UNKNOWN;

    public readonly LightspeedSessionManager _lightspeedSessionManager;
    
    public readonly EntryCarManager _entryCarManager;
    public readonly SessionManager _sessionManager;
    private readonly CSPFeatureManager _cspFeatureManager;
    private readonly CSPClientMessageTypeManager _cspClientMessageTypeManager;

    public Dictionary<string, clientEntryCarData> _entryCarQueueDictionary = new Dictionary<string, clientEntryCarData>();

    private static readonly string[] _forbiddenUsernameSubstrings = { "discord", "@", "#", ":", "```" };
    private static readonly string[] _forbiddenUsernames = { "everyone", "here" };

    public LightspeedPlugin(ACServerConfiguration serverConfiguration, EntryCarManager entryCarManager, SessionManager sessionManager, CSPFeatureManager cspFeatureManager, CSPClientMessageTypeManager cspClientMessageTypeManager)
    {
        Log.Information("------------------------------------");
        Log.Information("Starting: Lightspeed Plugin by Yhugi");
        Log.Information("Made for Lightspeed");
        Log.Information("discord.gg/dnzdf5E7Zb");
        Log.Information("------------------------------------");

        _entryCarManager = entryCarManager;
        _sessionManager = sessionManager;
        _cspFeatureManager = cspFeatureManager;
        _cspClientMessageTypeManager = cspClientMessageTypeManager;

        _lightspeedSessionManager = new LightspeedSessionManager(this);

        entryCarManager.ClientConnected += OnClientConnected;
        entryCarManager.ClientDisconnected += OnClientDisconnected;

        if(!Directory.Exists(ENTRY_CAR_DATA_DIR))
        {
            Directory.CreateDirectory(ENTRY_CAR_DATA_DIR);
        }

        if(!Directory.Exists(LIGHTSPEED_PLAYERS_DIR))
        {
            Directory.CreateDirectory(LIGHTSPEED_PLAYERS_DIR);
        }

        if(!File.Exists($"{ENTRY_CAR_DATA_DIR}/{SUMMARY_FILE}"))
        {
            File.Create($"{ENTRY_CAR_DATA_DIR}/{SUMMARY_FILE}");
        }

        System.Timers.Timer timer1 = new System.Timers.Timer();
        timer1.Elapsed += new System.Timers.ElapsedEventHandler(HandleClientEntryCars);
        timer1.Interval = 1000;
        timer1.Start();

        System.Timers.Timer timer2 = new System.Timers.Timer();
        timer2.Elapsed += new System.Timers.ElapsedEventHandler(HandleNewClientData);
        timer2.Interval = 10000;
        timer2.Start();

        /*System.Timers.Timer timer3 = new System.Timers.Timer();
        timer3.Elapsed += new System.Timers.ElapsedEventHandler(StartSummaryFileData);
        timer3.Interval = 20000;
        timer3.Start();*/

        _cspFeatureManager.Add(new CSPFeature { Name = "The Lightspeed Experience" });
        _cspFeatureManager.Add(new CSPFeature { Name = "VALID_DRIVERS" });

        string serverName = serverConfiguration.Server.Name;
        if(serverName.Contains(SERVER_NAME_CUT_UP))
        {
            Log.Information("Registered ClientMessage Overtake Score");
            _cspClientMessageTypeManager.RegisterClientMessageType(0x242096CD, IncomingScoreEnd);
        }
        else if(serverName.Contains(SERVER_NAME_DRIFT))
        {
            Log.Information("Registered ClientMessage Drift Score");
            _cspClientMessageTypeManager.RegisterClientMessageType(0x25BADD8D, IncomingScoreEnd);
        }

        //Example:
        //Cut Up
        //Lightspeed
        //SRP x JDM
        //Light Traffic
        //discord.gg/dnzdf5E7Zb
        //CA East 3
        string[] str = serverName.Split(" | ");
        string track = str[2];
        string traffic = "";
        if(str.Contains(SERVER_NAME_CUT_UP))
        {
            traffic = $" ∙ {str[3]}";
        }

        serverTag = $"{track}{traffic}";
    }

    private void IncomingScoreEnd(ACTcpClient client, PacketReader reader)
    {
        long score = reader.Read<long>();
        long car = reader.Read<long>();// TODO: how do I get the string?
        /*if (reader.Buffer.Length > reader.ReadPosition + 2)
        {
            string car = reader.ReadASCIIString(true);
        }*/

        Log.Information($"Score From: {SanitizeUsername(client.Name ?? NO_NAME)} - Score: {score}, Car (WIP): {car}");

        LightspeedSession? session = _lightspeedSessionManager.GetSession(client);
        if(session == null)
        {
            return;
        }

        if(score > session._personalBestScore)
        {
            session._personalBestScore = score;
            session._averageSpeedAtPb = session.GetAverageSpeed();
            session._carAtPb = client.EntryCar.Model; // TODO: maybe use "name" from "car/ui/ui_car.json" for each car and use that to display the name, copy each file to the dedi. Calculate each model and its real name on start to avoid performance problems
        }
    }

    private void OnClientConnected(ACTcpClient client, EventArgs args)
    {
        _lightspeedSessionManager.AddSession(client);
    }

    private void OnClientDisconnected(ACTcpClient client, EventArgs args)
    {
        _lightspeedSessionManager.RemoveSession(client);
    }

    private void HandleClientEntryCars(object? sender, EventArgs e)
    {
        foreach (var dictSession in _lightspeedSessionManager.GetSessions())
        {
            var session = dictSession.Value;
            if(session.GetType() == typeof(LightspeedSession))
            {
                ACTcpClient client = session._client;
                if(client.IsConnected && client.HasSentFirstUpdate)
                {
                    EntryCar car = client.EntryCar;
                    if(car != null)
                    {
                        int speedKmh = ((int) (car.Status.Velocity.Length() * 3.6f));
                        session.AddToAverageSpeed(speedKmh);
                        if(speedKmh > session.GetTopSpeed())
                        {
                            session.SetTopSpeed(speedKmh);
                        }
                    }
                }
            }
        }
    }

    private async void HandleNewClientData(object? sender, EventArgs e)
    {
        foreach (var entryCar in _entryCarQueueDictionary)
        {
            string model = entryCar.Key.Split(":")[0];
            clientEntryCarData data = entryCar.Value;
            string dir = $"{ENTRY_CAR_DATA_DIR}/{model}/{model}.json";
            if(File.Exists(dir))
            {
                FileStream stream = File.Open(dir, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                entryCarData? existingData = (entryCarData?) JsonSerializer.Deserialize(stream, typeof(entryCarData));
                if(stream.Length > 0 && existingData != null)
                {
                    existingData.TotalKmDriven += data.KmDriven;
                    if(data.TopSpeed > existingData.BestTopSpeed)
                    {
                        existingData.BestTopSpeed = data.TopSpeed;
                    }

                    stream.SetLength(0);
                }

                _entryCarQueueDictionary.Remove(entryCar.Key);

                await JsonSerializer.SerializeAsync(stream, existingData);
                await stream.DisposeAsync();
                stream.Close();
            }
        }
    }

    private async void StartSummaryFileData(object? sender, EventArgs e)
    {
        EntryCar[] cars = _entryCarManager.EntryCars;
        Dictionary<string, entryCarData> dict = new Dictionary<string, entryCarData>(cars.Count());

        foreach (EntryCar car in cars)
        {
            if(!car.AiControlled)
            {
                HandleEntryCarData(car);

                string model = car.Model;
                string dir = $"{ENTRY_CAR_DATA_DIR}/{model}/{model}.json";
                if(File.Exists(dir))
                {
                    FileStream stream = File.Open(dir, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                    entryCarData? existingData = (entryCarData?) JsonSerializer.Deserialize(stream, typeof(entryCarData));
                    if(stream.Length > 0 && existingData != null && !dict.ContainsKey(model))
                    {
                        dict.Add(model, existingData);
                    }

                    await stream.DisposeAsync();
                    stream.Close();
                }
            }
        }

        string dirB = $"{ENTRY_CAR_DATA_DIR}/{SUMMARY_FILE}";
        if(File.Exists(dirB))
        {
            FileStream streamB = File.Open(dirB, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            streamB.SetLength(0);
            await JsonSerializer.SerializeAsync(streamB, dict);
            await streamB.DisposeAsync();
            streamB.Close();
        }
    }

    private async void HandleEntryCarData(EntryCar car)
    {
        string commonDriver = NO_NAME;
        ACTcpClient? client = car.Client;
        if(client != null)
        {
            commonDriver = client.Name ?? NO_NAME;
        }

        entryCarData newData = new entryCarData(){
            TotalKmDriven = 0,
            BestTopSpeed = 0,
            MostCommonDriver = commonDriver,
        };

        string model = car.Model;
        string dir = $"{ENTRY_CAR_DATA_DIR}/{model}/";
        if(!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string newDir =  $"{dir}{model}.json";
        if(!File.Exists(newDir))
        {
            FileStream stream = File.Create(newDir);
            await JsonSerializer.SerializeAsync(stream, newData);
            await stream.DisposeAsync();
            stream.Close();
        }
    }

    public async void CreateClientForEntryCarData(EntryCar car, ACTcpClient client, LightspeedSession session)
    {
        string model = car.Model;
        string guid = client.Guid.ToString();
        string dir = $"{ENTRY_CAR_DATA_DIR}/{model}/{guid}/";
        if(!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        clientEntryCarData newData = new clientEntryCarData(){
            Username = session.getUsername(),
            KmDriven = session.CalculateDistanceDriven(),
            TimeSpent = session.CalculateTimeSpent(),
            TopSpeed = session.GetTopSpeed(),
        };

        string newDir = $"{dir}{guid}.json";
        FileStream stream;
        if(!File.Exists(newDir))
        {
            stream = File.Create(newDir);
        }
        else
        {
            stream = File.Open(newDir, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            clientEntryCarData? existingData = (clientEntryCarData?) JsonSerializer.Deserialize(stream, typeof(clientEntryCarData));
            if(stream.Length > 0 && existingData != null && existingData.GetType() == typeof(clientEntryCarData))
            {
                newData.KmDriven += existingData.KmDriven;
                newData.TimeSpent += existingData.TimeSpent;
                if(newData.TopSpeed < existingData.TopSpeed)
                {
                        newData.TopSpeed = existingData.TopSpeed;
                }
            }
        }

        stream.SetLength(0);
        
        await JsonSerializer.SerializeAsync(stream, newData);
        await stream.DisposeAsync();
        stream.Close();

        _entryCarQueueDictionary[$"{model}:{guid}"] = newData;
    }

    public static string SanitizeUsername(string name)
    {
        foreach (string str in _forbiddenUsernames)
        {
            if (name == str)
            {
                return $"_{str}";
            }
        }

        foreach (string str in _forbiddenUsernameSubstrings)
        {
            name = Regex.Replace(name, str, new string('*', str.Length), RegexOptions.IgnoreCase);
        }

        name = name.Substring(0, Math.Min(name.Length, 80));

        return name;
    }
}
