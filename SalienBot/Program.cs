using Newtonsoft.Json.Linq;
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

        public List<Clan> clans;
        public int rep_clan_lead;
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
        static int ROUND_TIME = 120;
        static int WAIT_TIME = 5;
        static int RE_TRIES = 3;
        static string ACCESS_TOKEN;
        public static int REP_CLAN = 148845;
        public static int START_ZONE = 45;
        public static List<Priority> PRIORITIES = new List<Priority>()
        {
            new Priority ("CODpL", 'L', '=', 0),
            new Priority ("CODPL", ' ', ' ', 0)
        };
        public static List<Zone> DEADLOCKS = new List<Zone>();

        static List<Planet> ActivePlanets = new List<Planet>();

        public static string BuildUrl(string method)
        {
            return "https://community.steam-api.com/" + method + "/v0001/";
        }

        static void Main(string[] args)
        {
            #region get access token
            try
            {
                string line = File.ReadAllLines("access_token.txt")[0];
                ACCESS_TOKEN = line;
            }
            catch (Exception e) { }
            if (ACCESS_TOKEN == null || ACCESS_TOKEN == "")
            {
                Console.WriteLine("access_token file empty?");
                Console.WriteLine("Please get a token from here: https://steamcommunity.com/saliengame/gettoken");
                Console.WriteLine("Past token here:");
                ACCESS_TOKEN = Console.ReadLine();
            }
            ACCESS_TOKEN.Trim('"', ' ', ',');
            if (ACCESS_TOKEN.Length != 32)
            {
                Console.WriteLine("Token seems wrong!");
                Console.WriteLine("Press Key to continue or close window.");
            }
            Console.WriteLine("Using Token: " + ACCESS_TOKEN);
            #endregion

            #region read priorities
            try
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
            catch (Exception e) { }
            #endregion

            while (true)
            {
                try
                {
                    Iteration();
                }
                catch (Exception e)
                {
                    Console.WriteLine("Exception: " + e.Message);
                    Console.WriteLine("Starting again...");
                }
            }
        }

        public static void Iteration()
        {
            RefreshData();

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

            /*JToken zone_join_resp = DoPostWithToken(BuildUrl("ITerritoryControlMinigameService/JoinZone"), "zone_position=" + bestZone.zone_position);
            if (!zone_join_resp.HasValues)
            {
                Console.WriteLine("Couldn't join zone " + bestZone.zone_position + "!");
                return;
            }*/

            int i = 0;
            while (true)
            {
                JToken zone_join_resp = DoPostWithToken(BuildUrl("ITerritoryControlMinigameService/JoinZone"), "zone_position=" + bestZone.zone_position);
                if (zone_join_resp.HasValues)
                {
                    break;
                }

                Console.WriteLine("Couldn't join zone " + bestZone.zone_position + "!");
                Console.WriteLine("Trying again in " + WAIT_TIME + " seconds.");

                Thread.Sleep(1000 * WAIT_TIME);

                if (i >= RE_TRIES)
                {
                    DEADLOCKS.Add(bestZone);
                    return;
                }

                i++;
            }

            Console.WriteLine("Joined zone " + bestZone.zone_position + " in planet " + playerInfo.active_planet.name);

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("------------------------------");
            Console.WriteLine("Current zone captured: " + (bestZone.capture_progress * 100).ToString("#.##") + "%");
            Console.WriteLine("Current planet captured: " + (playerInfo.active_planet.capture_progress * 100).ToString("#.##") + "%");
            Console.WriteLine("Current planet players: " + playerInfo.active_planet.current_players);
            Console.WriteLine("------------------------------");
            Console.ResetColor();

            Console.WriteLine("Sleeping for " + ROUND_TIME + " seconds...");
            Thread.Sleep(1000 * ROUND_TIME);

            ReportScore(GetScoreFromDifficulty(bestZone.difficulty));
        }

        private static void ReportScore(int score)
        {
            Console.WriteLine("Reporting score " + score + "...");

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
                    total_joins = (int)planet["state"]["total_joins"],
                    current_players = (int)planet["state"]["current_players"],
                    availableZones = new List<Zone>()
                };

                JToken planet_response = DoGet(BuildUrl("ITerritoryControlMinigameService/GetPlanet") + "/?id=" + p.id);
                var zones = planet_response["planets"].First["zones"];

                foreach (JToken zone in zones)
                {
                    if (!(bool)zone["captured"] && !ContainsDeadlock((int)zone["gameid"]))
                    {
                        Zone z = new Zone
                        {
                            planet_id = p.id,

                            zone_position = (int)zone["zone_position"],
                            zone_id = (int)zone["gameid"],
                            difficulty = (int)zone["difficulty"],
                            capture_progress = (double)zone["capture_progress"],
                            clans = new List<Clan>(),
                            rep_clan_lead = 5
                        };

                        if (z.zone_position < START_ZONE)
                            z.zone_offset = START_ZONE - z.zone_position;
                        else
                            z.zone_offset = z.zone_position - START_ZONE;

                        var clans = zone["top_clans"];
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

                        p.availableZones.Add(z);
                    }
                }

                ActivePlanets.Add(p);
            }
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
                    default:
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
                active_planet = null
            };

            try
            {
                pi.time_on_planet = (int)response["time_on_planet"];
                pi.active_planet = ActivePlanets.Find(x => x.id == (int)response["active_planet"]);
            }
            catch (Exception e) { }

            return pi;
        }

        public static void RepresentClan()
        {
            DoPostWithToken(BuildUrl("ITerritoryControlMinigameService/RepresentClan"), "clanid=" + REP_CLAN);
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
