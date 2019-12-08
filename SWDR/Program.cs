using HtmlAgilityPack;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SWDR
{
    class Program
    {
        private const string STEAM_WORKSHOP_URL_PATTERN = @"(?:https:\/\/steamcommunity\.com\/sharedfiles\/filedetails\/\?id=)?(\d+)";
        private static bool _hasArgumentSettings = false;

        static void Main(string[] args)
        {
            ulong parameterWorkshopId = default;
            uint parameterAppId = default;

            if (args.Length >= 2)
            {
                try
                {
                    parameterWorkshopId = ulong.Parse(args[1]);
                    parameterAppId = uint.Parse(args[0]);

                    _hasArgumentSettings = true;
                }
                catch { } // Ignore and fail silently
            }

            var appVersion = Assembly.GetExecutingAssembly().GetName().Version;

            if (!_hasArgumentSettings)
            {
                Console.WriteLine($"Steam Workshop Dependency Resolver - v{appVersion.Major}.{appVersion.Minor}.{appVersion.Build} (C) {DateTime.UtcNow.Year} Ryan Torzynski");
                Console.WriteLine("CTRL+C to exit application.");
                Console.WriteLine();

                Write(" WARNING ", ConsoleColor.Black, ConsoleColor.DarkYellow);
                WriteLine(" Initializing Steam with placeholder app id");
                InitializeSteam(480);
            }
            else
            {
                Write(" WARNING ", ConsoleColor.Black, ConsoleColor.DarkYellow);
                WriteLine($" Reinitializing Steam with target app id {parameterAppId}");
                InitializeSteam(parameterAppId);
            }

            while (true)
            {
                ulong workshopId = default;

                while (true)
                {
                    string input = "";

                    if (_hasArgumentSettings)
                    {
                        input = parameterWorkshopId.ToString();
                        _hasArgumentSettings = false;
                    }
                    else
                    {
                        Console.WriteLine();
                        input = ReadLine.Read("> Please enter a workshop URL or ID (CTRL+C to exit): ");
                    }

                    var match = Regex.Match(input, STEAM_WORKSHOP_URL_PATTERN);

                    if (!match.Success)
                    {
                        WriteLine("Invalid workshop URL or ID", ConsoleColor.Red);
                    }


                    try
                    {
                        workshopId = ulong.Parse(match.Groups[1].Value);
                    }
                    catch
                    {
                        continue;
                    }

                    break;
                }


                var item = SteamUGC.QueryFileAsync(workshopId).GetAwaiter().GetResult();
                var workshopItem = item.Value;

                if (item.Value.ConsumerApp != SteamClient.AppId)
                {
                    Console.WriteLine();
                    RerunWithWorkshopId(item.Value.ConsumerApp, item.Value.Id);
                    return;
                }

                Console.WriteLine();
                Console.Write("Fetching workshop item information... ");

                if (!item.HasValue || !item.Value.IsPublic)
                {
                    Write(" FAIL ", ConsoleColor.White, ConsoleColor.DarkRed);
                    WriteLine($" Workshop item with ID {workshopId} not found or private.", ConsoleColor.DarkRed);
                    continue;
                }

                Write(" SUCCESS ", ConsoleColor.Black, ConsoleColor.DarkGreen);
                WriteLine(" !");
                Console.WriteLine();

                Write("> Is ");
                Write(workshopItem.Title, ConsoleColor.DarkYellow);

                string isCorrectAnswer = ReadLine.Read(" the correct workshop item? (y/n): ");

                if (!isCorrectAnswer.ToLower().StartsWith("y"))
                {
                    continue;
                }

                Console.WriteLine();
                Console.WriteLine("Fetching dependencies of workshop item... ");
                Console.WriteLine();

                PrintItemStatus(workshopItem);

                var dependencies = new List<Steamworks.Ugc.Item> { workshopItem };
                GetDependenciesRecursive(workshopId, dependencies, true);

                var requiredDependencies = dependencies.Where(x => !x.IsSubscribed).ToList();

                Console.WriteLine();
                WriteLine($"{dependencies.Count} dependencies found, {dependencies.Count - requiredDependencies.Count()} subscribed, {requiredDependencies.Count()} need to be subscribed");

                if (requiredDependencies.Count() == 0)
                {
                    WriteLine("");
                    WriteLine("Workshop item and all dependencies already installed.", ConsoleColor.DarkGreen);
                    continue;
                }

                isCorrectAnswer = ReadLine.Read("> Would you like to automatically subscribe and install all missing dependencies? (y/n): ");

                if (!isCorrectAnswer.ToLower().StartsWith("y"))
                {
                    continue;
                }

                Console.WriteLine();
                Console.Write("Subscribing to missing dependencies... ");

                bool hadError = false;

                foreach (var requiredDependency in requiredDependencies)
                {
                    bool success = requiredDependency.Subscribe().GetAwaiter().GetResult();

                    if (!success)
                    {
                        hadError = true;
                    }
                }

                if (hadError)
                {
                    Write(" ERROR ", ConsoleColor.White, ConsoleColor.DarkRed);
                    WriteLine(" Atleast one item could not be subscribed to.");
                }
                else
                {
                    Write(" SUCCESS ", ConsoleColor.Black, ConsoleColor.DarkGreen);
                    WriteLine(" !");
                }

                Console.WriteLine("Downloading dependencies... ");
                Console.WriteLine();

                var progressBar = new ProgressBar(60);
                double totalProgress = requiredDependencies.Count();

                while (true)
                {
                    Task.Delay(TimeSpan.FromMilliseconds(100)).Wait();
                    double currentProgress = requiredDependencies.Sum(x => x.DownloadAmount);

                    progressBar.Update((currentProgress / totalProgress) * 100);

                    if (currentProgress >= totalProgress) break;
                }

                Write(" ");
                Write(" SUCCESS ", ConsoleColor.Black, ConsoleColor.DarkGreen);
                WriteLine(" !");
                Console.WriteLine();
                Console.WriteLine();

                WriteLine("Item and dependencies successfully installed!", ConsoleColor.DarkGreen);
            }
        }

        static void InitializeSteam(uint appId)
        {
            SteamClient.Init(appId);

            if (!SteamClient.IsLoggedOn)
            {
                WriteLine("Steam not running or user not logged in. Log into Steam to use this application.", ConsoleColor.Red);
                WriteLine("");
                WriteLine("Press ANY key to exit...");
                Console.ReadLine();
                Environment.Exit(1);
            }
        }

        static void RerunWithWorkshopId(uint appId, ulong workshopId)
        {
            var psi = new ProcessStartInfo($"{Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0])}.exe");

            psi.Arguments = $"{appId.ToString()} {workshopId.ToString()}";

            var childProcess = Process.Start(psi);

            childProcess.WaitForExit();
            Environment.Exit(0);
        }

        static void GetDependenciesRecursive(ulong id, List<Steamworks.Ugc.Item> dependencies, bool noisy = false)
        {
            var workshopItemWeb = new HtmlWeb().Load($"https://steamcommunity.com/sharedfiles/filedetails/?id={id}");
            var dependencyDiv = workshopItemWeb.GetElementbyId("RequiredItems");

            if (dependencyDiv == null) return;

            foreach (var childDiv in dependencyDiv.ChildNodes.Where(x => x.Name == "a"))
            {
                string dependencyUrl = childDiv.GetAttributeValue("href", "");
                var match = Regex.Match(dependencyUrl, STEAM_WORKSHOP_URL_PATTERN);
                ulong dependencyId = ulong.Parse(match.Groups[1].Value);

                var dependencyItem = SteamUGC.QueryFileAsync(dependencyId).GetAwaiter().GetResult().Value;

                if (dependencies.Count(x => x.Id == dependencyItem.Id) > 0) continue;

                if (noisy)
                {
                    PrintItemStatus(dependencyItem);
                }

                dependencies.Add(dependencyItem);
                GetDependenciesRecursive(dependencyItem.Id, dependencies, noisy);
            }
        }

        static void PrintItemStatus(Steamworks.Ugc.Item dependencyItem)
        {
            if (dependencyItem.IsInstalled && dependencyItem.IsSubscribed)
            {
                Write("   INSTALLED   ", ConsoleColor.Black, ConsoleColor.DarkGreen);
                WriteLine($" {dependencyItem.Title}");
            }
            else if (dependencyItem.IsDownloading)
            {
                Write("  DOWNLOADING  ", ConsoleColor.Black, ConsoleColor.Yellow);
                WriteLine($" {dependencyItem.Title}");
            }
            else if (dependencyItem.IsDownloadPending)
            {
                Write("    PENDING    ", ConsoleColor.Black, ConsoleColor.DarkYellow);
                WriteLine($" {dependencyItem.Title}");
            }
            else
            {
                Write(" NOT INSTALLED ", ConsoleColor.White, ConsoleColor.DarkRed);
                WriteLine($" {dependencyItem.Title}");
            }
        }

        static void Write(string text, ConsoleColor? color = null, ConsoleColor? backgroundColor = null)
        {
            Console.ResetColor();

            if (color != null)
            {
                Console.ForegroundColor = color.Value;
            }

            if (backgroundColor != null)
            {
                Console.BackgroundColor = backgroundColor.Value;
            }

            Console.Write(text);

            Console.ResetColor();
        }

        static void WriteLine(string text, ConsoleColor? color = null, ConsoleColor? backgroundColor = null)
        {
            Write(text, color, backgroundColor);
            Console.WriteLine();
        }
    }
}
