using MongoDB.Bson;
using MongoDB.Driver;

var uri = Environment.GetEnvironmentVariable("MONGODB_URI")
          ?? "mongodb://localhost:27017/?directConnection=true";
var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "ingest";

var client = new MongoClient(uri);
var db = client.GetDatabase("cement_plant");

if (!db.ListCollectionNames().ToList().Contains("telemetry"))
{
    db.CreateCollection("telemetry", new CreateCollectionOptions
    {
        TimeSeriesOptions = new TimeSeriesOptions(
            timeField: "ts", metaField: "sensor", granularity: TimeSeriesGranularity.Seconds)
    });
    Console.WriteLine("Created time series collection: cement_plant.telemetry");
}
var coll = db.GetCollection<BsonDocument>("telemetry");

switch (mode)
{
    case "ingest": Ingest(); break;
    case "query": RunQueries(); break;
    default: Console.WriteLine("Usage: dotnet run [ingest|query]"); break;
}

void Ingest()
{
    var equipment = new (string Id, string Area, string Type)[]
    {
        ("kiln-1",        "pyroprocessing",  "rotary_kiln"),
        ("preheater-1",   "pyroprocessing",  "preheater_tower"),
        ("cooler-1",      "pyroprocessing",  "clinker_cooler"),
        ("raw-mill-1",    "raw_grinding",    "vertical_mill"),
        ("cement-mill-1", "finish_grinding", "ball_mill"),
        ("cement-mill-2", "finish_grinding", "ball_mill"),
        ("hopper-1",      "raw_materials",   "hopper"),
        ("hopper-2",      "raw_materials",   "hopper"),
        ("clinker-silo-1","storage",         "silo"),
    };

    var lineMode = Console.IsOutputRedirected;
    long total = 0;
    Console.WriteLine($"Ingesting cement plant telemetry ({equipment.Length} sensors, 1 reading/sec each)");
    Console.WriteLine($"  target: {uri}");

    while (true)
    {
        var now = DateTime.UtcNow;
        var batch = equipment.Select(e => BuildReading(e, now)).ToList();
        try
        {
            coll.InsertMany(batch);
            total += batch.Count;
            var status = $"[{now:HH:mm:ss}] inserted={total:N0}  kiln-1 burning zone={State["kiln-1.temp"]:F1} degC";
            if (lineMode) { if (total % (batch.Count * 10) == 0) Console.WriteLine(status); }
            else Console.Write($"\r{status}   ");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[{now:HH:mm:ss}] write failed ({ex.GetType().Name}: {FirstLine(ex.Message)}) -- retrying...");
            Thread.Sleep(2000);
        }
        Thread.Sleep(1000);
    }
}

BsonDocument BuildReading((string Id, string Area, string Type) e, DateTime ts)
{
    var doc = new BsonDocument
    {
        { "ts", ts },
        { "sensor", new BsonDocument
            {
                { "plant", "monterrey" },
                { "area", e.Area },
                { "equipment", e.Id },
                { "type", e.Type },
            }
        },
    };

    switch (e.Type)
    {
        case "rotary_kiln":
            doc["burningZoneTempC"] = Walk($"{e.Id}.temp", 1450, 6, 1380, 1520);
            doc["shellTempC"]       = Walk($"{e.Id}.shell", 310, 2, 280, 360);
            doc["rpm"]              = Walk($"{e.Id}.rpm", 3.5, 0.05, 2.8, 4.2);
            break;
        case "preheater_tower":
            doc["cycloneTempC"] = Walk($"{e.Id}.temp", 890, 4, 820, 950);
            doc["draftPa"]      = Walk($"{e.Id}.draft", -5200, 40, -6000, -4500);
            break;
        case "clinker_cooler":
            doc["outletTempC"]   = Walk($"{e.Id}.temp", 105, 3, 80, 160);
            doc["grateSpeedSpm"] = Walk($"{e.Id}.speed", 11, 0.2, 8, 14);
            break;
        case "vertical_mill":
        case "ball_mill":
            doc["powerKw"]      = Walk($"{e.Id}.power", 4200, 35, 3600, 4800);
            doc["vibrationMmS"] = Walk($"{e.Id}.vib", 4.5, 0.15, 2.5, 8.0);
            doc["outletTempC"]  = Walk($"{e.Id}.temp", 95, 1.5, 80, 115);
            break;
        case "hopper":
        case "silo":
            doc["levelPct"] = Walk($"{e.Id}.level", 62, 0.8, 5, 98);
            break;
    }
    return doc;
}

void RunQueries()
{
    Console.WriteLine($"Total readings: {coll.EstimatedDocumentCount():N0}\n");

    Console.WriteLine("Per-equipment averages (last 5 minutes):");
    Console.WriteLine($"{"equipment",-16}{"readings",10}{"temp degC",12}{"power kW",12}{"level %",10}");
    var since = DateTime.UtcNow.AddMinutes(-5);
    var perEquipment = coll.Aggregate<BsonDocument>(new[]
    {
        new BsonDocument("$match", new BsonDocument("ts", new BsonDocument("$gte", since))),
        new BsonDocument("$group", new BsonDocument
        {
            { "_id", "$sensor.equipment" },
            { "count", new BsonDocument("$sum", 1) },
            { "avgTempC", new BsonDocument("$avg", new BsonDocument("$ifNull",
                new BsonArray { "$burningZoneTempC", "$cycloneTempC", "$outletTempC", BsonNull.Value })) },
            { "avgPowerKw", new BsonDocument("$avg", "$powerKw") },
            { "avgLevelPct", new BsonDocument("$avg", "$levelPct") },
        }),
        new BsonDocument("$sort", new BsonDocument("_id", 1)),
    }).ToList();
    foreach (var d in perEquipment)
    {
        Console.WriteLine($"{d["_id"].AsString,-16}{d["count"].ToInt64(),10:N0}" +
                          $"{Num(d, "avgTempC"),12}{Num(d, "avgPowerKw"),12}{Num(d, "avgLevelPct"),10}");
    }

    Console.WriteLine("\nkiln-1 burning zone temperature, per minute (last 10 minutes):");
    var trend = coll.Aggregate<BsonDocument>(new[]
    {
        new BsonDocument("$match", new BsonDocument
        {
            { "sensor.equipment", "kiln-1" },
            { "ts", new BsonDocument("$gte", DateTime.UtcNow.AddMinutes(-10)) },
        }),
        new BsonDocument("$group", new BsonDocument
        {
            { "_id", new BsonDocument("$dateTrunc", new BsonDocument { { "date", "$ts" }, { "unit", "minute" } }) },
            { "avg", new BsonDocument("$avg", "$burningZoneTempC") },
            { "max", new BsonDocument("$max", "$burningZoneTempC") },
        }),
        new BsonDocument("$sort", new BsonDocument("_id", 1)),
    }).ToList();
    foreach (var d in trend)
    {
        var avg = d["avg"].ToDouble();
        var bar = new string('#', (int)Math.Clamp((avg - 1380) / 3, 0, 50));
        Console.WriteLine($"{d["_id"].ToUniversalTime():HH:mm}  avg={avg,7:F1}  max={d["max"].ToDouble(),7:F1}  {bar}");
    }
}

double Walk(string key, double start, double step, double min, double max)
{
    var v = State.TryGetValue(key, out var cur) ? cur : start;
    v = Math.Clamp(v + (Rng.NextDouble() * 2 - 1) * step, min, max);
    State[key] = v;
    return Math.Round(v, 2);
}

string Num(BsonDocument d, string field) =>
    d[field].IsBsonNull ? "-" : d[field].ToDouble().ToString("F1");

string FirstLine(string s) => s.Split('\n')[0].Trim();

static partial class Program
{
    static readonly Random Rng = new();
    static readonly Dictionary<string, double> State = new();
}
