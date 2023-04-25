using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using LightspeedPlugin.Session;
using Serilog;

namespace LightspeedPlugin;

[ApiController]
public class LightspeedController : ControllerBase
{
    LightspeedPlugin _plugin;

    private class clientScore
    {
        public string Name { get; set; } = LightspeedPlugin.NO_NAME;
        public string Server { get; set; } = LightspeedPlugin.UNKNOWN;
        public long Score { get; set; } = 0;
        public long AverageSpeedKmh { get; set; } = 0;
        public string Car { get; set; } = LightspeedPlugin.UNKNOWN;
    }

    public LightspeedController(LightspeedPlugin plugin)
    {
        _plugin = plugin;
    }

    [HttpGet("/stats")]
    public string Stats()
    {
        string file = $"{LightspeedPlugin.ENTRY_CAR_DATA_DIR}/{LightspeedPlugin.SUMMARY_FILE}";
        if(!System.IO.File.Exists(file))
        {
            return "empty";
        }

        FileStream stream = System.IO.File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        string contents;
        using(var sr = new StreamReader(stream))
        {
            contents = sr.ReadToEnd();
        }

        stream.Dispose();
        stream.Close();

        return contents;
    }

    [HttpGet("/scores")]
    public string Scores()
    {
        List<clientScore> list = new List<clientScore>();
        foreach (string dir in Directory.GetDirectories(LightspeedPlugin.LIGHTSPEED_PLAYERS_DIR))
        {
            string file = $"{dir}/{dir.Split('/').Last()}.json";
            if(System.IO.File.Exists(file))
            {
                
                FileStream stream = System.IO.File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                if(stream.Length > 0)
                {
                    LightspeedSession.clientData? existingData = (LightspeedSession.clientData?) JsonSerializer.Deserialize(stream, typeof(LightspeedSession.clientData));
                    if(existingData != null && existingData.GetType() == typeof(LightspeedSession.clientData))
                    {
                        list.Add(new clientScore(){ Name = existingData.Username, Server = _plugin.serverTag, Score = existingData.PersonalBestScore, AverageSpeedKmh = existingData.AverageSpeedKmh, Car = existingData.Car });
                    }

                    stream.Dispose();
                    stream.Close();
                }
            }
        }

        return JsonSerializer.Serialize(list);
    }
}