﻿using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SalienBot
{
    class PlayerInfo
    {
        public int time_on_planet;
        public Planet active_planet;
        public int exp;
        public int next_level_exp;
        public int level;
        public int clanid;
    }

    class Zone
    {
        public int planet_id;
        public int planet_clan_captured;

        public int zone_position;
        public int zone_id;
        public int zone_offset;
        public int difficulty;
        public double capture_progress;

        public int active_boss;

        public List<Clan> clans;
        public int rep_clan_lead;

        public DateTime deadlock_time;
    }

    class Planet
    {
        public int id;
        public string name;
        public int difficulty;
        public double capture_progress;
        public int clan_captured;
        public int total_joins;
        public int current_players;

        public List<Zone> availableZones;
    }

    class Clan
    {
        public int id;
        public string name;
    }

    class Priority
    {
        public string order;
        public char check_type;
        public char check_comp;
        public int check_val;

        public Priority(string Order, char Check_Type, char Check_Comp, int Check_Val)
        {
            this.order = Order;
            this.check_type = Check_Type;
            this.check_comp = Check_Comp;
            this.check_val = Check_Val;
        }
    }

    class Program
    {
        public static string CONFIG_FILE_PATH = "config.txt";

        static int ROUND_TIME = 120;
        static int WAIT_TIME = 5;
        static int RE_TRIES = 3;
        static Exception LAST_EXCEPTION = new Exception("NO EXCEPTION OCCURRED YET");
        static int WAIT_TIME2 = 300;
        static int RE_TRIES2 = 2;
        static int RE_TRIES2_COUNT = 0;
        static string ACCESS_TOKEN = "";
        public static int REP_CLAN = 148845;
        public static int STEAMID = 0;
        public static int START_ZONE = 45;
        public static List<Priority> PRIORITIES = new List<Priority>()
        {
            new Priority ("Pb", 'B', '=', 1),
            new Priority ("CODpL", 'L', '=', 0),
            new Priority ("CODPL", ' ', ' ', 0)
        };
        public static List<Zone> DEADLOCKS = new List<Zone>();

        static List<Planet> ActivePlanets = new List<Planet>();

        public static Random rnd = new Random();

        public static string BuildUrl(string method)
        {
            return "https://community.steam-api.com/" + method + "/v0001/";
        }

        static void Main(string[] args)
        {
            GetConfigFromFile();

            //handling of old files

            if (ACCESS_TOKEN.Length != 32)
            {   
                if (File.Exists("access_token.txt"))
                {
                    ACCESS_TOKEN = File.ReadAllLines("access_token.txt")[0];
                }
                else
                {
                    Console.WriteLine("No access_token in file?");
                    Console.WriteLine("Please get a token from here: https://steamcommunity.com/saliengame/gettoken");
                    Console.WriteLine("Paste token down here:");
                    ACCESS_TOKEN = Console.ReadLine();
                }
                ACCESS_TOKEN.Trim(' ', '"', ',', '.');
                if (ACCESS_TOKEN.Length != 32)
                {
                    Console.WriteLine("Token seems wrong!: {0}", ACCESS_TOKEN);
                    Console.WriteLine("Press Key to continue or close window.");
                    Console.Read();
                }
            }
            if (File.Exists("access_token.txt")) { File.Delete("access_token.txt"); }

            if (File.Exists("priorities.txt"))
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("Old properties file found.");
                Console.WriteLine("Read from there? Y = Yes.");
                var key = Console.ReadKey().KeyChar;
                if (key == 'Y' || key == 'y')
                {
                    string[] lines = File.ReadAllLines("priorities.txt");

                    if (lines.Length > 0)
                    {
                        List<Priority> prios = new List<Priority>();

                        for (int i = 0; i < lines.Length; i++)
                        {
                            if (lines[i].Length >= 7)
                            {
                                string[] split = lines[i].Split(',');
                                if (split.Length == 4)
                                {
                                    prios.Add(new Priority(split[0].Trim(), Convert.ToChar(split[1]), Convert.ToChar(split[2]), Convert.ToInt32(split[3])));
                                }
                                else
                                {
                                    Console.WriteLine("wrong format, check line " + i + " in priorities file, skipping.");
                                }
                            }
                            else
                            {
                                Console.WriteLine("wrong format, check line " + i + " in priorities file, skipping.");
                            }

                        }

                        if (prios.Count > 0)
                        {
                            PRIORITIES.Clear();
                            PRIORITIES.AddRange(prios);
                        }

                    }
                }
                File.Delete("priorities.txt");

                Console.ResetColor();
            }

            if (STEAMID == 0)
            {
                Console.WriteLine("No SteamID in file! It is not necessary, but");
                Console.WriteLine("please get it from here: https://steamcommunity.com/saliengame/gettoken");
                Console.WriteLine("Paste ID down here: (Or just hit enter.)");
                string line = Console.ReadLine();
                if (line.Length > 3)
                {
                    try
                    {
                        STEAMID = SteamIDparse(Convert.ToInt64(line.Trim('"')));
                    }
                    catch (Exception e) { Console.WriteLine("Not a number? Skip.."); }
                }
            }

            WriteConfigToFile();

            while (true)
            {
                try
                {
                    Iteration();
                }
                catch (Exception e)
                {
                    ExceptionHandling(e);
                }
            }
        }

        public static void Iteration()
        {
            
            GetConfigFromFile();
            RefreshData();
            //removes zone older than 10 min from DEADLOCK list
            DEADLOCKS.RemoveAll(z => z.deadlock_time <= DateTime.Now.Subtract(TimeSpan.FromMinutes(10)));

            Zone bestZone = DeterminateBestZoneAndPlanet();
            PlayerInfo playerInfo = GetPlayerInfo();
            if (playerInfo.clanid != REP_CLAN) RepresentClan();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("------------------------------");
            if (playerInfo.active_planet != null)
                Console.WriteLine("Time on planet '" + playerInfo.active_planet.name + "': " + playerInfo.time_on_planet + "s");
            Console.WriteLine("Level: " + playerInfo.level);
            Console.WriteLine("XP: " + playerInfo.exp + "/" + playerInfo.next_level_exp + "  (" + (((double)playerInfo.exp / (double)playerInfo.next_level_exp) * 100).ToString("#.##") + "%)");
            Console.WriteLine("------------------------------");
            Console.ResetColor();

            // Leave planet if necessary
            if (playerInfo.active_planet != null && playerInfo.active_planet.id != bestZone.planet_id)
            {
                Console.WriteLine("Leaving planet " + playerInfo.active_planet.name + "...");
                DoPostWithToken(BuildUrl("IMiniGameService/LeaveGame"), "gameid=" + playerInfo.active_planet.id);
                playerInfo.active_planet = null;
            }
            // Join planet if necessary
            if (playerInfo.active_planet == null || playerInfo.active_planet.id != bestZone.planet_id)
            {
                playerInfo.active_planet = ActivePlanets.Find(x => x.id == bestZone.planet_id);
                Console.WriteLine("Joining planet " + playerInfo.active_planet.name + "...");
                DoPostWithToken(BuildUrl("ITerritoryControlMinigameService/JoinPlanet"), "id=" + bestZone.planet_id);
            }

            int i = 0;
            while (true)
            {
                if (bestZone.active_boss != 1)
                {
                    JToken zone_join_resp = DoPostWithToken(BuildUrl("ITerritoryControlMinigameService/JoinZone"), "zone_position=" + bestZone.zone_position);
                    if (zone_join_resp.HasValues)
                    {
                        break;
                    }
                }
                else
                {
                    JToken zone_join_resp = DoPostWithToken(BuildUrl("ITerritoryControlMinigameService/JoinBossZone"), "zone_position=" + bestZone.zone_position);
                    if (zone_join_resp.HasValues)
                    {
                        break;
                    }
                }
                
                Console.WriteLine("Couldn't join zone " + bestZone.zone_position + "!");
                
                if (i >= RE_TRIES)
                {
                    bestZone.deadlock_time = DateTime.Now;
                    DEADLOCKS.Add(bestZone);
                    return;
                }
                else
                {
                    SleepCountdown(1000 * WAIT_TIME * (i + 1), "Trying again in:");
                }

                i++;
            }

            if (bestZone.active_boss != 1)
            {

                Console.WriteLine("Joined zone " + bestZone.zone_position + "/" + ZoneIDToCoord(bestZone.zone_position) + " in planet " + playerInfo.active_planet.name);

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("------------------------------");
                Console.WriteLine("Current zone captured: " + (bestZone.capture_progress * 100).ToString("#.##") + "%");
                Console.WriteLine("Current zone leaders: " + ClansToString(bestZone.clans, 3));
                Console.WriteLine("Current planet captured: " + (playerInfo.active_planet.capture_progress * 100).ToString("#.##") + "%");
                Console.WriteLine("Current planet players: " + playerInfo.active_planet.current_players);
                Console.WriteLine("------------------------------");
                Console.ResetColor();
                SleepCountdown(1000 * ROUND_TIME, "Waiting for round to end:");

                ReportScore(GetScoreFromDifficulty(bestZone.difficulty));

            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Joined BOSS zone " + bestZone.zone_position + "/" + ZoneIDToCoord(bestZone.zone_position) + " in planet " + playerInfo.active_planet.name);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("------------------------------");

                int heal_cooldown = 18;
                int use_heal = 0;
                int l = 1;

                while (true)
                {
                    Thread.Sleep(5000);
                    if (heal_cooldown == 0)
                    {
                        heal_cooldown = 18;
                        use_heal = 1;
                    }
                    else { use_heal = 0; }
                    JToken boss_resp = DoPostWithToken(BuildUrl("ITerritoryControlMinigameService/ReportBossDamage"), "use_heal_ability=" + use_heal +"&damage_to_boss=" + rnd.Next(1, 40) + "&damage_taken=" + 0);
                    if (!boss_resp.HasValues)
                    {
                        if (l > RE_TRIES)
                        {
                            Console.WriteLine();
                            Console.WriteLine("To many errors, retry.");
                            Console.WriteLine("------------------------------");
                            Console.ResetColor();
                            break;
                        }
                        l++;
                        continue;
                    }
                    if ((bool)boss_resp["waiting_for_players"]) { continue; }
                    if ((bool)boss_resp["game_over"])
                    {
                        Console.WriteLine();
                        Console.WriteLine("Bossfight over.");
                        Console.WriteLine("------------------------------");
                        Console.ResetColor();
                        break;
                    }

                    string status = "Boss HP: " +(string)boss_resp["boss_status"]["boss_hp"] + "/" + (string)boss_resp["boss_status"]["boss_max_hp"];
                    
                    JToken players = boss_resp.SelectToken("boss_status").SelectToken("boss_players");
                    foreach (JToken p in players)
                    {
                        if ((int)p["accountid"] == STEAMID)
                        {
                            status += " | Your HP: " + (string)p["hp"] + "/" + (string)p["max_hp"] + " XP:" + (string)p["xp_earned"];
                        }
                    }
                    Console.Write("\r{0}   ", status);
                    heal_cooldown--;
                }
            }
        }

        private static void ReportScore(int score)
        {
            Console.Write("Reporting score " + score + "... ");

            JToken token = DoPostWithToken(BuildUrl("ITerritoryControlMinigameService/ReportScore"), "score=" + score + "&language=english");

            if (token.HasValues)
            {
                int new_score = (int)token["new_score"];
                int old_score = (int)token["old_score"];
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Earned " + (new_score - old_score));
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine("Couldn't report score! :(");
            }
        }

        private static int GetScoreFromDifficulty(int difficulty)
        {
            if (difficulty == 1)
                return 600;
            else if (difficulty == 2)
                return 1200;
            else
                return 2400;
        }

        public static void RefreshData()
        {
            ActivePlanets.Clear();

            JToken response = DoGet(BuildUrl("ITerritoryControlMinigameService/GetPlanets") + "/?active_only=1&language=english");
            var planets = response.SelectToken("planets");

            foreach (JToken planet in planets)
            {
                Planet p = new Planet
                {
                    id = (int)planet["id"],
                    name = (string)planet["state"]["name"],
                    difficulty = (int)planet["state"]["difficulty"],
                    capture_progress = (double)planet["state"]["capture_progress"],
                    clan_captured = 0,
                    total_joins = (int)planet["state"]["total_joins"],
                    current_players = (int)planet["state"]["current_players"],
                    availableZones = new List<Zone>()
                };

                JToken planet_response = DoGet(BuildUrl("ITerritoryControlMinigameService/GetPlanet") + "/?id=" + p.id);
                var zones = planet_response["planets"].First["zones"];

                foreach (JToken zone in zones)
                {
                    if (zone["gameid"] == null || zone["zone_position"] == null || zone["difficulty"] == null  || zone["capture_progress"] == null) { continue; }
                    if ((bool)zone["captured"])
                    {
                        if ((int)zone["leader"]["accountid"] == REP_CLAN) p.clan_captured += 1;
                    }
                    else if (!ContainsDeadlock((int)zone["gameid"]))
                    {
                        Zone z = new Zone
                        {
                            planet_id = p.id,

                            zone_position = (int)zone["zone_position"],
                            zone_id = (int)zone["gameid"],
                            difficulty = (int)zone["difficulty"],
                            capture_progress = (double)zone["capture_progress"],
                            active_boss = 0,
                            clans = new List<Clan>(),
                            rep_clan_lead = 5
                        };

                        if ((int)zone["type"] == 4 && (bool)zone["boss_active"])
                        {
                            z.active_boss = 1;
                        }

                        if (z.zone_position < START_ZONE)
                            z.zone_offset = START_ZONE - z.zone_position;
                        else
                            z.zone_offset = z.zone_position - START_ZONE;

                        var clans = zone["top_clans"];
                        if (clans != null)
                        {
                            var i = 0;
                            foreach (JToken ct in clans)
                            {
                                Clan c = new Clan
                                {
                                    id = (int)ct["accountid"],
                                    name = (string)ct["name"]
                                };

                                if (c.id == REP_CLAN) z.rep_clan_lead = i;

                                z.clans.Add(c);

                                i++;
                            }
                        }
                        

                        p.availableZones.Add(z);
                    }
                }

                foreach (Zone z in p.availableZones)
                {
                    z.planet_clan_captured = p.clan_captured;
                }
                ActivePlanets.Add(p);
            }
        }

        public static void GetConfigFromFile()
        {
            try
            {
                string[] lines;

                if (File.Exists(CONFIG_FILE_PATH)) {  lines = File.ReadAllLines(CONFIG_FILE_PATH); }
                else { return; }

                if (lines.Length > 0)
                {
                    int i = 0;
                    foreach (string Line in lines)
                    {
                        i++;

                        Console.ForegroundColor = ConsoleColor.DarkRed;

                        if (Line.Length >= 7)
                        {
                            string[] split = Line.Split(':');
                            switch (split[0])
                            {
                                case "ROUND_TIME":
                                    ROUND_TIME = Convert.ToInt32(split[1].Trim('"', ' '));
                                    break;
                                case "WAIT_TIME":
                                    WAIT_TIME = Convert.ToInt32(split[1].Trim('"', ' '));
                                    break;
                                case "RE_TRIES":
                                    RE_TRIES = Convert.ToInt32(split[1].Trim('"', ' '));
                                    break;
                                case "WAIT_TIME2":
                                    WAIT_TIME2 = Convert.ToInt32(split[1].Trim('"', ' '));
                                    break;
                                case "RE_TRIES2":
                                    RE_TRIES2 = Convert.ToInt32(split[1].Trim('"', ' '));
                                    break;
                                case "ACCESS_TOKEN":
                                    ACCESS_TOKEN = split[1].Trim('"', ' ');
                                    break;
                                case "REP_CLAN":
                                    REP_CLAN = Convert.ToInt32(split[1].Trim('"', ' '));
                                    break;
                                case "STEAMID":
                                    STEAMID = SteamIDparse(Convert.ToInt64(split[1].Trim('"', ' ')));
                                    break;
                                case "START_ZONE":
                                    START_ZONE = Convert.ToInt32(split[1].Trim('"', ' '));
                                    break;
                                case "PRIORITIES":
                                    string[] Configs = split[1].Trim('"', ' ').Split(';');
                                    List<Priority> prios = new List<Priority>();

                                    foreach (string config in Configs)
                                    {
                                        if (config.Length >= 7)
                                        {
                                            string[] values = config.Split(',');
                                            if (values.Length == 4)
                                            {
                                                prios.Add(new Priority(values[0].Trim(), Convert.ToChar(values[1]), Convert.ToChar(values[2]), Convert.ToInt32(values[3])));
                                            }
                                            else
                                            {
                                                Console.WriteLine("wrong format, check line " + i + " in config file, skipping.");
                                            }
                                        }
                                        else
                                        {
                                            Console.WriteLine("wrong format, check line " + i + " in config file, skipping.");
                                        }

                                    }

                                    if (prios.Count > 0)
                                    {
                                        PRIORITIES.Clear();
                                        PRIORITIES.AddRange(prios);
                                    }

                                    break;
                                default:
                                    Console.WriteLine("fournd no Configuration for " + split[0] + "in line " + i + " in config file, skipping.");
                                    break;

                            }
                           
                        }
                        else
                        {
                            Console.WriteLine("wrong format, check line " + i + " in config file, skipping.");
                        }
                    }
                }

                Console.ResetColor();
            }
            catch (Exception e)
            {
                Console.ForegroundColor = ConsoleColor.DarkRed;
                Console.WriteLine("unexpected error occured in getMainConfig " + e );
                Console.ResetColor();
                ExceptionHandling(e);
            }
        }

        public static void WriteConfigToFile()
        {
            FileStream stream = File.Open(CONFIG_FILE_PATH, FileMode.Create);

            StreamWriteLine("ROUND_TIME:\""+ ROUND_TIME + "\"", stream);
            StreamWriteLine("WAIT_TIME:\""+ WAIT_TIME + "\"", stream);
            StreamWriteLine("RE_TRIES:\""+ RE_TRIES + "\"", stream);
            StreamWriteLine("WAIT_TIME2:\""+ WAIT_TIME2 + "\"", stream);
            StreamWriteLine("RE_TRIES2:\""+ RE_TRIES2 + "\"", stream);
            StreamWriteLine("ACCESS_TOKEN:\""+ ACCESS_TOKEN + "\"", stream);
            StreamWriteLine("REP_CLAN:\""+ REP_CLAN + "\"", stream);
            StreamWriteLine("STEAMID:\""+ STEAMID + "\"", stream);
            StreamWriteLine("START_ZONE:\""+ START_ZONE + "\"", stream);

            string prios = "";
            foreach (Priority p in PRIORITIES)
            {
                prios += p.order + "," + p.check_type + "," + p.check_comp + "," + p.check_val + ";";
            }
            StreamWriteLine("PRIORITIES:\""+ prios.TrimEnd(';') + "\"", stream);

            stream.Close();
        }

        public static Zone DeterminateBestZoneAndPlanet()
        {
            List<Zone> allZones = new List<Zone>();

            foreach (Planet p in ActivePlanets)
            {
                allZones.AddRange(p.availableZones);
            }

            //var result = allZones.OrderBy(c => c.difficulty).ThenBy(c => c.planet_priority);
            allZones = allZones.OrderBy(l => l.planet_id).ToList();

            foreach (Priority p in PRIORITIES)
            {
                foreach (Char c in p.order)
                {
                    switch (c)
                    {
                        case 'P':
                            allZones = allZones.OrderBy(l => l.capture_progress).ToList();
                            break;
                        case 'p':
                            allZones = allZones.OrderByDescending(l => l.capture_progress).ToList();
                            break;
                        case 'L':
                            allZones = allZones.OrderBy(l => l.rep_clan_lead).ToList();
                            break;
                        case 'l':
                            allZones = allZones.OrderByDescending(l => l.rep_clan_lead).ToList();
                            break;
                        case 'D':
                            allZones = allZones.OrderBy(l => l.difficulty).ToList();
                            break;
                        case 'd':
                            allZones = allZones.OrderByDescending(l => l.difficulty).ToList();
                            break;
                        case 'O':
                            allZones = allZones.OrderBy(l => l.zone_offset).ToList();
                            break;
                        case 'o':
                            allZones = allZones.OrderByDescending(l => l.zone_offset).ToList();
                            break;
                        case 'C':
                            allZones = allZones.OrderBy(l => l.planet_clan_captured).ToList();
                            break;
                        case 'c':
                            allZones = allZones.OrderByDescending(l => l.planet_clan_captured).ToList();
                            break;
                        case 'B':
                            allZones = allZones.OrderBy(l => l.active_boss).ToList();
                            break;
                        case 'b':
                            allZones = allZones.OrderByDescending(l => l.active_boss).ToList();
                            break;
                    }
                }

                switch (p.check_type)
                {
                    case 'P':
                        if (BestZoneCheck((int)allZones.First().capture_progress * 100, p.check_comp, p.check_val)) return allZones.First();
                        break;
                    case 'L':
                        if (BestZoneCheck(allZones.First().rep_clan_lead, p.check_comp, p.check_val)) return allZones.First();
                        break;
                    case 'D':
                        if (BestZoneCheck(allZones.First().difficulty, p.check_comp, p.check_val)) return allZones.First();
                        break;
                    case 'O':
                        if (BestZoneCheck(allZones.First().zone_offset, p.check_comp, p.check_val)) return allZones.First();
                        break;
                    case 'C':
                        if (BestZoneCheck(allZones.First().planet_clan_captured, p.check_comp, p.check_val)) return allZones.First();
                        break;
                    case 'B':
                        if (BestZoneCheck(allZones.First().active_boss, p.check_comp, p.check_val)) return allZones.First();
                        break;
                }

            }

            return allZones.First();
        }

        public static bool BestZoneCheck(int ZoneValue, char CompType, int CompValue)
        {
            bool check = false;

            switch (CompType)
            {
                case '<':
                    if (ZoneValue <= CompValue) check = true;
                    break;
                case '>':
                    if (ZoneValue >= CompValue) check = true;
                    break;
                case '=':
                    if (ZoneValue == CompValue) check = true;
                    break;
                case '!':
                    if (ZoneValue != CompValue) check = true;
                    break;
            }

            return check;
        }

        public static bool ContainsDeadlock(int gameid)
        {
            bool contains = false;
            
            foreach (Zone z in DEADLOCKS)
            {
                if (z.zone_id == gameid)
                {
                    contains = true;
                    break;
                }
            }

            return contains;
        }
        
        public static PlayerInfo GetPlayerInfo()
        {
            JToken response = DoPostWithToken(BuildUrl("ITerritoryControlMinigameService/GetPlayerInfo"));

            PlayerInfo pi = new PlayerInfo
            {
                exp = (int)response["score"],
                next_level_exp = (int)response["next_level_score"],
                level = (int)response["level"],
                time_on_planet = 0,
                active_planet = null,
                clanid = 0
            };

            try
            {
                pi.time_on_planet = (int)response["time_on_planet"];
                pi.active_planet = ActivePlanets.Find(x => x.id == (int)response["active_planet"]);
            }
            catch (Exception e) { }

            try
            {
                pi.clanid = (int)response["clan_info"]["accountid"];
            }
            catch (Exception e) { }

            return pi;
        }

        public static void RepresentClan()
        {
            DoPostWithToken(BuildUrl("ITerritoryControlMinigameService/RepresentClan"), "clanid=" + REP_CLAN);
        }

        public static string ClansToString(List<Clan> Clans, int Count)
        {
            string clanstring = "";
            int counter = 1;

            foreach (Clan c in Clans)
            {
                clanstring += c.name + "; ";

                if (counter >= Count) { break; }
                counter++;
            }

            return clanstring.TrimEnd(';', ' ');
        }

        public static void ExceptionHandling(Exception exception)
        {
            Console.ForegroundColor = ConsoleColor.Red;

            //compare to last exception
            if (exception.Message == LAST_EXCEPTION.Message) { RE_TRIES2_COUNT++; }
            else { RE_TRIES2_COUNT = 1; }

            Console.WriteLine("Exception encountered, check log for details.");

            //write log
            try
            {
                //var file = File.OpenWrite("log.txt");
                var file = new FileStream("log.txt", FileMode.Append);
                StreamWriteLine("#-----------------------------", file);
                StreamWriteLine("Time: " + DateTime.Now.ToString(), file);
                StreamWriteLine("Exception count: " + RE_TRIES2_COUNT, file);
                StreamWriteLine(exception.ToString(), file);
                StreamWriteLine(exception.InnerException.StackTrace, file);
                StreamWriteLine("#-----------------------------", file);
                file.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception while writing log file:");
                Console.WriteLine(exception.Message);
                Console.WriteLine(e);
            }

            //set last exception
            LAST_EXCEPTION = exception;

            //wait and return
            if (RE_TRIES2_COUNT > RE_TRIES)
            {
                SleepCountdown(1000 * WAIT_TIME2 * Math.Min(RE_TRIES2_COUNT - RE_TRIES, 3), "Trying again in:");
            }
            else
            {
                SleepCountdown(1000 * WAIT_TIME * RE_TRIES2_COUNT, "Trying again in:");
            }

            Console.ResetColor();
        }

        public static void StreamWriteLine(string data, FileStream stream, int start = 0, int end = -1)
        {
            if (end > data.Length || end == -1)
            {
                end = data.Length;
            }
            if (start > data.Length-1 || start == end)
            {
                start = end - 1;
            }
            
            stream.Write(Encoding.ASCII.GetBytes(data), start, end);

            byte[] newline = Encoding.ASCII.GetBytes(Environment.NewLine);
            stream.Write(newline, 0, newline.Length);
        }

        public static string ZoneIDToCoord(int ZoneID)
        {
            int row = 1;
            int col = 65;
            int cnt = 0;

            for (int j = 1; j <= 8; j++)
            {
                for (int i = 1; i <= 12; i++)
                {
                    if (ZoneID == cnt) { return Convert.ToChar(col) + Convert.ToString(row); }
                    col++;
                    cnt++;
                }
                col = 65;
                row++;
            }

            return "err";
            //return Convert.ToChar(((ZoneID+1) % 12) + 64) + Convert.ToString(((ZoneID + 1) % 12));
        }

        public static void SleepCountdown(int Time, string Message)
        {
            int time_left = Time;
            string message_time;

            while (time_left > 0)
            {
                message_time = Message + " " +  (time_left / 1000) + " seconds...";

                Console.Write("\r{0}   ", message_time);

                Thread.Sleep(Math.Min(1000, time_left));

                time_left = time_left - Math.Min(1000, Time);
            }

            message_time = Message + " " + (Time / 1000) + " seconds... Done.";
            Console.WriteLine("\r{0}   ", message_time);
        }

        public static int SteamIDparse(Int64 ID64)
        {
            if (ID64 > 76561197960265728) { ID64 -= 76561197960265728; }

            return (int)ID64;
        }

        public static JToken ParseResponse(string response)
        {
            return JObject.Parse(response).SelectToken("response");
        }

        public static JToken DoPostWithToken(string url, string post_data = "")
        {
            return DoPost(url, post_data + (post_data.Length > 0 ? "&" : "") + "access_token=" + ACCESS_TOKEN);
        }

        public static JToken DoGet(string url)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            var response = (HttpWebResponse)request.GetResponse();
            return ParseResponse(new StreamReader(response.GetResponseStream()).ReadToEnd());
        }

        public static JToken DoPost(string url, string post_data)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            var data = Encoding.ASCII.GetBytes(post_data);

            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            request.ContentLength = data.Length;

            using (var stream = request.GetRequestStream())
            {
                stream.Write(data, 0, data.Length);
            }

            var response = (HttpWebResponse)request.GetResponse();

            return ParseResponse(new StreamReader(response.GetResponseStream()).ReadToEnd());
        }
    }
}
