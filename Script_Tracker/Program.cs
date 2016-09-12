﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using YAMLHelper;
using System.IO;

namespace Script_Tracker
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Starting server.");

            LoadDatabase();

            Task.Factory.StartNew(() =>
            {
                while (true)
                {
                    try
                    {
                        IRCBot bottymcbotface = new IRCBot();
                        bottymcbotface.Start("irc.frenetic.xyz", 6667, "#script-tracker");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.ToString());
                    }
                }
            });
            Task.Factory.StartNew(() =>
            {
                while (true)
                {
                    try
                    {
                        IRCBot bottymcbotface = new IRCBot();
                        bottymcbotface.Start("irc.esper.net", 6667, "#denizen-dev");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.ToString());
                    }
                }
            });

            var listener = new HttpListener();
            //listener.Prefixes.Add("http://localhost:8099/");
            listener.Prefixes.Add("http://*:8099/");
            //listener.Prefixes.Add("http://127.0.0.1:10123/");

            listener.Start();

            while (true)
            {
                HttpListenerContext context = null;
                try
                {
                    context = listener.GetContext();
                    context.Response.StatusCode = 200;
                    string page = context.Request.Url.LocalPath.Before("?");
                    switch (page)
                    {
                        case "/tracker":
                            HandleTrackerInput(context);
                            break;
                        case "/scripts":
                            webPages.getScriptPage(context);
                            break;
                        case "/popular":
                            webPages.getPopularPage(context);
                            break;
                     }
                }
                catch(Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                    if (context != null)
                    {
                        byte[] data = Encoding.UTF8.GetBytes("FAILURE! Unable to process your request."); // better 404
                        context.Response.OutputStream.Write(data, 0, data.Length);
                    }
                }
                finally
                {
                    if (context != null)
                    {
                        context.Response.OutputStream.Close();
                    }
                }
            }
        }
        static List<Script> ScriptTable = new List<Script>();
        public static KeyValuePair<int, int> LoadDatabase()
        {
            HttpClient client = new HttpClient();
            int i = 0;
            List<string> authors = new List<string>();
            List<Script> templist = new List<Script>();
            try
            {
                while (true)
                {
                    string result = client.GetStringAsync("http://one.denizenscript.com/denizen/repo/entry/" + i).Result;
                    if (result == null)
                    {
                        throw new Exception("result is null!");
                    }
                    string name = result.After("<title>").Before(" by ");
                    if ((name == "") || (name.StartsWith("Invalid paste number")))
                    {
                        throw new Exception("No title found!");
                    }
                    string author = result.After(" by ").Before(" ");
                    List<string> publicdata = new List<string>();
                    if (result.Contains("&lt;--script-tracker--&gt;") && result.Contains("&lt;--!script-tracker--&gt;"))
                    {
                        string arguments = result.After("&lt;--script-tracker--&gt;").Before("&lt;--!script-tracker--&gt;");
                        Dictionary<string, string> arglist = new Dictionary<string, string>();
                        foreach (string argumentvalue in arguments.Split(new string[] { "<br>" }, StringSplitOptions.RemoveEmptyEntries)) {
                            string[] split = argumentvalue.SplitFast('=', 2);
                            arglist.Add(split[0], split[1]);
                        }
                        if (arglist.ContainsKey("public_data"))
                        {
                            publicdata = new List<string>(arglist["public_data"].SplitFast(','));
                        }
                    }
                    Script script = new Script(i, name, author, publicdata);
                    templist.Add(script);
                    if (!authors.Contains(script.Author))
                    {
                        authors.Add(script.Author);
                    }
                    Console.WriteLine("loaded script: " + i);
                    i++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
            KeyValuePair<int, int> output = new KeyValuePair<int, int>(i, authors.Count);
            ScriptTable = templist;
            return output;
        }
        public static void HandleTrackerInput(HttpListenerContext request)
        {
            if (string.IsNullOrWhiteSpace(request.Request.QueryString["script"]))
            {
                byte[] data = Encoding.UTF8.GetBytes("FAILURE! No script specified!");
                request.Response.OutputStream.Write(data, 0, data.Length);
                return;
            }
            int ID = int.Parse(request.Request.QueryString["script"]);
            Console.WriteLine(request.Request.Headers["X-Forwarded-For"].ToString());
            string address = request.Request.RemoteEndPoint.ToString().Replace(".", "-"); // this returns the local IP, pls2fix
            Script script = GetScript(ID);
            if (script == null)
            {
                byte[] data = Encoding.UTF8.GetBytes("FAILURE! This script ID does not match our database.");
                request.Response.OutputStream.Write(data, 0, data.Length);
                return;
            }
            if (!script.FloodControl.ContainsKey(address))
            {
                script.FloodControl[address] = new KeyValuePair<int, DateTime>(0, DateTime.Now);
            }
            else if ((script.FloodControl[address].Key > 5) && (DateTime.Now.Subtract(script.FloodControl[address].Value).TotalMinutes < 10))
            {
                byte[] data = Encoding.UTF8.GetBytes("FAILURE! don't force feed me!");
                request.Response.OutputStream.Write(data, 0, data.Length);
                return;
            }
            script.FloodControl[address] = new KeyValuePair<int, DateTime>(script.FloodControl[address].Key + 1, DateTime.Now);
            DateTime timestamp = DateTime.Now;
            string fileID = GetFileIDForTimestamp(timestamp).ToString();
            YAMLConfiguration log = getlog(fileID);
            foreach (string queryKey in request.Request.QueryString.Keys)
            {
                log.Set(timestamp.Hour + "." + script.ID + "." + address + "." + queryKey, request.Request.QueryString[queryKey]);
            }
            Directory.CreateDirectory("logs/");
            File.WriteAllText("logs/" + fileID + ".yml", log.SaveToString());
            byte[] data2 = Encoding.UTF8.GetBytes("SUCCESS! We successfully recieved your data. Thank your for your contribution.");
            request.Response.OutputStream.Write(data2, 0, data2.Length);
        }
        static Dictionary<string, YAMLConfiguration> LoadedLogs = new Dictionary<string, YAMLConfiguration>();
        public static Script GetScript(int ID)
        {
            foreach (Script script in ScriptTable)
            {
                if (script.ID == ID)
                {
                    return script;
                }
            }
            return null;
        }
        public static Script GetScript(string search)
        {
            int searchbyint;
            if (int.TryParse(search, out searchbyint))
            {
                return GetScript(searchbyint);
            }
            else if (string.IsNullOrWhiteSpace(search))
            {
                return null;
            }
            search = search.ToLowerFast();
            Script bestmatch = null;
            foreach (Script script in ScriptTable)
            {
                string name = script.Name.ToLowerFast();
                if (name == search)
                {
                    return script;
                }
                else if (name.StartsWith(search))
                {
                    bestmatch = script;
                }
                else if (bestmatch == null && name.Contains(search))
                {
                    bestmatch = script;
                }
            }
            return bestmatch;
        }

        public static List<Script> getScriptsByAuthor(string author)
        {
            author = author.ToLowerFast();
            List<Script> scripts = new List<Script>();
            foreach (Script script in ScriptTable)
            {
                if (script.Author.ToLowerFast() == author)
                {
                    scripts.Add(script);
                }
            }
            return scripts;
        }


        public static string PublicDataAsString(Script script)
        {
            string publicdata = "";
            foreach (string datavalue in script.PublicData)
            {
                publicdata += ", " + datavalue;
            }
            if (publicdata != "")
            {
                publicdata = publicdata.Substring(2);
                publicdata = publicdata.Replace("<br>", "");
            }
            else
            {
                publicdata = "None";
            }
            return publicdata;
        }

        public static YAMLConfiguration getlog(string fileID)
        {
            YAMLConfiguration log;
            if (!LoadedLogs.ContainsKey(fileID))
            {
                if (File.Exists("logs/" + fileID + ".yml"))
                {
                    log = new YAMLConfiguration(File.ReadAllText("logs/" + fileID + ".yml"));
                }
                else
                {
                    LoadedLogs[fileID] = log = new YAMLConfiguration("");
                }
            }
            else
            {
                log = LoadedLogs[fileID];
            }
            return log;
        }



        public static List<KeyValuePair<Script, int>> getpopular(int amount)
        {
            List<KeyValuePair<Script, int>> popular  = new List<KeyValuePair<Script, int>>();
            DateTime timestamp = DateTime.Now.AddHours(-1);
            string fileID = GetFileIDForTimestamp(timestamp).ToString();
            YAMLConfiguration log = getlog(fileID);
            foreach (string value in log.GetKeys(timestamp.Hour.ToString()))
            {
                Script script = GetScript(int.Parse(value));
                int servers = log.GetKeys(timestamp.Hour + "." + script.ID).Count;
                popular.Add(new KeyValuePair<Script, int>(script, servers));
            }
            popular.Sort((one, two) => two.Value.CompareTo(one.Value));
            return popular.GetRange(0, amount > popular.Count ? popular.Count : amount);
        }


        public static string getsearchbar()
        {
            string searchbar = "<link rel=\"stylesheet\" href=\"https://maxcdn.bootstrapcdn.com/bootstrap/3.3.6/css/bootstrap.min.css\" integrity=\"sha384-1q8mTJOASx8j1Au+a5WDVnPi2lkFfwwEAa8hDDdjZlpLegxhjVME1fgjWPGmkzs7\" crossorigin=\"anonymous\"> <script> function dosearch() { script = document.getElementById(\"script_name\").value; data = document.getElementById(\"data_input\").value; datavalue = document.getElementById(\"data_value\").value; if (script == \"\") { alert(\"No script specified!\"); return; } query = \"?script=\" + script; if (data != \"\") { query = query + \"&data=\" + data; } if (datavalue != \"\") { query = query + \"&datavalue=\" + datavalue; } window.location.replace(\"http://morphanone.space:10123/scripts/script\" + query); } </script> <nav class=\"navbar navbar-default navbar-fixed-top\"> <div class=\"container\" style=\"padding-bottom: 7px\"> <div id=\"navbar\" class=\"navbar-collapse collapse\"> <div class=\"row\"> <div class=\"col-md-10 col-md-offset-1\"> <div class=\"form-group\"> <label for=\"script_name\">Name</label> <input type=\"text\" class=\"form-control\" id=\"script_name\" placeholder=\"Script name\"> </div> </div> </div> <div class=\"row text-center\"> <div class=\"form-inline\"> <div class=\"form-group\"> <label for=\"data_input\">Data</label> <input type=\"text\" class=\"form-control\" id=\"data_input\" placeholder=\"Data\"> </div> <div class=\"form-group\"> <label for=\"mode_input\" data-toggle=\"tooltip\" data-placement=\"bottom\" title=\"Tooltip on bottom\">Mode</label> <input type=\"text\" class=\"form-control\" id=\"mode_input\" placeholder=\"Mode\"> </div> <div class=\"form-group\"> <label for=\"data_value\">Data Value</label> <input type=\"text\" class=\"form-control\" id=\"data_value\" placeholder=\"Data Value\"> </div> <div class=\"form-group\"> <label for=\"days_input\">Days</label> <input type=\"text\" class=\"form-control\" id=\"days_input\" placeholder=\"Days\"> </div> <button type=\"submit\" class=\"btn btn-primary\" onclick=\"dosearch()\">Search</button> </div> </div> </div> </div> </nav>";
            return searchbar;
        }



        public static long GetFileIDForTimestamp(DateTime timestamp)
        {
            timestamp = timestamp.ToUniversalTime();
            return ((timestamp.Ticks / TimeSpan.TicksPerMillisecond / 1000 / 60 / 60 / 24) * 24 * 60 * 60 * 1000);
        }





    }
}