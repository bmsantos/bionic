using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using McMaster.Extensions.CommandLineUtils;

namespace Bionic {
  enum ProjectType {
    Standalone,
    HostedServer,
    HostedClient,
    Unknown
  }

  struct ProjectInfo {
    public string path;
    public string filename;
    public string dir;
    public ProjectType projectType;
  }

  static class ExtMethods {
    public static bool IsNullOrEmpty<T>(this IEnumerable<T> me) => !me?.Any() ?? true;
  }

  [Command(Description = "🤖 Bionic - An Ionic CLI clone for Blazor projects")]
  class Program {
    private static readonly List<string> commandOptions = new List<string> {"docs", "generate", "info", "serve", "start",  "uninstall", "update"};

    private static readonly List<string> generateOptions = new List<string>
      {"component", "page", "provider", "service"};

    private static readonly string AppCssPath = "App.scss";
    private static readonly string ProgramPath = "Program.cs";

    private static readonly Regex ServiceRegEx =
      new Regex(@"BrowserServiceProvider[^(]*\([\s]*(.*?)=>[\s]*{([^}]*)}", RegexOptions.Compiled);

    private static string adjustedDir;

    [Argument(0, Description = "Project Command (docs, generate, info, serve, start, uninstall, update)")]
    private string command { get; set; }

    [Argument(1, Description = "Command Option")]
    private string option { get; set; }

    [Argument(2, Description = "Artifact Name")]
    private string artifact { get; set; }

    // Commands
    [Option("-s|--start", Description = "Prepares Blazor project to mimic Ionic structure")]
    private bool start { get; set; } = false;

    [Option("-g|--generate", Description = "Generate components, pages, and providers/services")]
    private bool generate { get; set; } = false;

    [Option("-v|--version", Description = "Bionic version")]
    private bool version { get; } = false;

    [Option("-u|--update", Description = "Bionic update")]
    private bool update { get; } = false;

    [Option("-un|--uninstall", Description = "Uninstall Bionic")]
    private bool uninstall { get; } = false;

    public static int Main(string[] args) => CommandLineApplication.Execute<Program>(args);

    private int OnExecute() {
      if (version) return Version();

      if (command == "docs") return OpenBlazorDocs();
      if (command == "info") return Info();
      if (command == "serve") return ServeBlazor();
      if (start || command == "start") return SetupBionic();
      if (update || command == "update") return UpdateBionic();
      if (uninstall || command == "uninstall") return UninstallBionic();

      if (generate) {
        if (command != null) {
          if (option != null) {
            artifact = option;
          }

          option = command;
        }

        command = "generate";
      }

      if (command == "generate") generate = true;

      if (!commandOptions.Contains(command)) {
        Console.WriteLine("☠  You must provide a valid project command!");
        Console.WriteLine($"   Available Project Commands: {string.Join(", ", commandOptions)}");
        return 1;
      }

      if (IsGenerateCommandComplete()) GenerateArtifact();
      else return 1;

      return 0;
    }

    private bool IsGenerateCommandComplete() {
      if (option != null && !generateOptions.Contains(option)) {
        Console.WriteLine($"☠  Can't generate \"{option}\"");
        Console.WriteLine($"   You can only generate: {string.Join(", ", generateOptions)}");
        return false;
      }

      while (!generateOptions.Contains(option)) {
        option = Prompt.GetString("What would you like to generate?\n (component, page or provider): ",
          promptColor: ConsoleColor.DarkGreen);
      }

      while (artifact == null) {
        artifact = Prompt.GetString($"How would you like to name your {option}?",
          promptColor: ConsoleColor.DarkGreen);
      }

      return true;
    }

    private static int SetupBionic() {
      Console.WriteLine($"🤖  Preparing your Bionic Project...");

      // 1. Get project file name
      var projectFiles = GetProjectFiles();
      if (projectFiles.IsNullOrEmpty()) {
        Console.WriteLine($"☠ No C# project found. Please make sure you are in the root of a C# project.");
        return 1;
      }

      var currentDir = Directory.GetCurrentDirectory();

      foreach (var pi in projectFiles) {
        if (pi.projectType == ProjectType.Unknown) continue;

        Directory.SetCurrentDirectory(pi.dir);

        if (pi.projectType != ProjectType.HostedServer) {
          // 2. Create App.scss
          var alreadyStarted = InitAppCss();

          if (alreadyStarted) {
            alreadyStarted = Prompt.GetYesNo(
              "Project seems to have already been started. Are you sure you want to continue?",
              false,
              promptColor: ConsoleColor.DarkGreen
            );
            if (!alreadyStarted) {
              Console.WriteLine("Ok! Bionic start canceled.");
              return 0;
            }
          }

          // 3. Inject App.css in index.html
          InjectAppCssInIndexHtml();

          // 4. Inject targets in .csproj
          IntroduceProjectTargets(pi);

          // 5. Install Bionic Templates
          InstallBionicTemplates();
        }
        else {
          // 1. Its Hosted ... Inject targets in .csproj
          var client = projectFiles.FirstOrDefault(p => p.projectType == ProjectType.HostedClient);
          if (client.filename == null) {
            Console.WriteLine("☠  Unable to start project. Client directory for Hosted Blazor project was not found.");
            return 1;
          }
          IntroduceProjectTargets(pi, Path.GetRelativePath(pi.dir, client.dir));
        }

        Directory.SetCurrentDirectory(currentDir);
      }

      if (adjustedDir != null) Directory.SetCurrentDirectory(adjustedDir);

      return 0;
    }

    private static int InstallBionicTemplates() => DotNet("new -i BionicTemplates");

    private static int ServeBlazor() => DotNet("watch run");

    private static int UpdateBionic() => DotNet("tool update -g Bionic");

    private static int UninstallBionic() => DotNet("tool uninstall -g Bionic");

    private static int OpenBlazorDocs() {
      var browser = OpenUrl("https://blazor.net");
      browser?.WaitForExit();
      return browser?.ExitCode ?? 1;
    }

    private void GenerateArtifact() {
      Console.WriteLine($"🚀  Generating a {option} named {artifact}");

      if (option == "page") {
        Process.Start(
          DotNetExe.FullPathOrDefault(),
          $"new bionic.{option} -n {artifact} -p /{ToPageName(artifact)} -o ./{ToCamelCase(option)}s"
        )?.WaitForExit();
        IntroduceAppCssImport($"{ToCamelCase(option)}s", artifact);
      }
      else if (option == "component") {
        Process.Start(
          DotNetExe.FullPathOrDefault(),
          $"new bionic.{option} -n {artifact} -o ./{ToCamelCase(option)}s"
        )?.WaitForExit();
        IntroduceAppCssImport($"{ToCamelCase(option)}s", artifact);
      }
      else if (option == "provider" || option == "service") {
        Process.Start(
          DotNetExe.FullPathOrDefault(),
          $"new bionic.{option} -n {artifact} -o ./{ToCamelCase(option)}s"
        )?.WaitForExit();
        IntroduceServiceInBrowser(artifact);
      }
    }

    private static bool InitAppCss() {
      if (File.Exists(AppCssPath)) return true;

      using (var sw = File.CreateText(AppCssPath)) {
        sw.WriteLine("// WARNING - This file is automatically updated by Bionic CLI, please do not remove");
        sw.WriteLine("\n// Components\n\n// Pages\n");
      }

      return false;
    }

    private static string ToPageName(string artifact) {
      var rx = new Regex("[pP]age");
      var name = rx.Replace(artifact, "").ToLower();
      return string.IsNullOrEmpty(name) ? artifact.ToLower() : name;
    }

    private static void IntroduceAppCssImport(string type, string artifactName) {
      SeekForLineStartingWithAndInsert(AppCssPath, $"// {type}", $"@import \"{type}/{artifactName}.scss\";");
    }

    private static void IntroduceProjectTargets(ProjectInfo projectInfo, string relativePath = "") {
      string watcher = string.Format(@"
    <ItemGroup>
        <Watch Include=""{0}**/*.cshtml;{0}**/*.scss"" Visible=""false""/>
    </ItemGroup>", relativePath.IsNullOrEmpty() || relativePath.EndsWith("/") ? relativePath : $"{relativePath}/");

      const string scssCompiler = @"
    <Target Name=""CompileSCSS"" BeforeTargets=""Build"" Condition=""Exists('App.scss')"">
        <Message Importance=""high"" Text=""Compiling SCSS"" />
        <Exec Command=""scss --no-cache --update ./App.scss:./wwwroot/css/App.css"" />
    </Target>";

      string content = null;

      switch (projectInfo.projectType) {
        case ProjectType.Standalone:
          content = $"{watcher}\n\n{scssCompiler}";
          break;
        case ProjectType.HostedServer:
          content = $"{watcher}";
          break;
        case ProjectType.HostedClient:
          content = $"{scssCompiler}";
          break;
        case ProjectType.Unknown:
          return;
        default:
          return;
      }

      SeekForLineStartingWithAndInsert(projectInfo.filename, "</Project>", content, false);
    }

    private static void InjectAppCssInIndexHtml() {
      SeekForLineStartingWithAndInsert(
        "wwwroot/index.html",
        "    <link href=\"css/site.css",
        "    <link href=\"css/App.css\" rel=\"stylesheet\" />",
        false
      );
    }

    private static void IntroduceServiceInBrowser(string serviceName) {
      var text = new StringBuilder();

      string all = File.ReadAllText(ProgramPath);

      var matches = ServiceRegEx.Matches(all);
      var browserName = matches[0].Groups[1].Value.Trim().Trim(Environment.NewLine.ToCharArray());
      var currentServices = matches[0].Groups[2].Value;
      var currentServicesList = currentServices.Split("\n");
      var lastEntry = currentServicesList.Last();
      var newServices =
        $"{currentServices}    {browserName}.AddSingleton<I{serviceName}, {serviceName}>();\n{lastEntry}";

      using (var file = new StreamWriter(File.Create(ProgramPath))) {
        file.Write(all.Replace(currentServices, newServices));
      }
    }

    private static string ToCamelCase(string str) {
      return string.IsNullOrEmpty(str) || str.Length < 1 ? "" : char.ToUpperInvariant(str[0]) + str.Substring(1);
    }

    private static void SeekForLineStartingWithAndInsert(string fileName, string startsWith,
      string contentToIntroduce, bool insertAfter = true) {
      var text = new StringBuilder();

      foreach (var s in File.ReadAllLines(fileName)) {
        text.AppendLine(s.StartsWith(startsWith)
          ? (insertAfter ? $"{s}\n{contentToIntroduce}" : $"{contentToIntroduce}\n{s}")
          : s);
      }

      using (var file = new StreamWriter(File.Create(fileName))) {
        file.Write(text.ToString());
      }
    }

    private static ProjectInfo[] GetProjectFiles(bool onParent = false) {
      var projectInfoList = GetProjectInfoList();
      if (projectInfoList.Length == 1 && projectInfoList[0].projectType != ProjectType.Standalone && !onParent) {
        adjustedDir = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory("../");
        projectInfoList = GetProjectFiles(true);
      }
      return projectInfoList;
    }

    private static ProjectInfo[] GetProjectInfoList() {
      var projectFiles = Directory.GetFiles("./", "*.csproj", SearchOption.AllDirectories);
      return projectFiles.ToList().ConvertAll(path => {
        var pi = new ProjectInfo {path = path};
        pi.dir = Path.GetDirectoryName(path);
        pi.filename = Path.GetFileName(path);
        pi.projectType = ProjectType.Unknown;
        if (FileContains(path, "Microsoft.AspNetCore.Blazor.Cli")) {
          // Standalone
          pi.projectType = ProjectType.Standalone;
        }
        else if (FileContains(path, "Microsoft.AspNetCore.Blazor.Server")) {
          // Hosted Project - Server
          pi.projectType = ProjectType.HostedServer;
        }
        else if (FileContains(path, "Microsoft.AspNetCore.Blazor.Build")) {
          // Hosted Project - Client
          pi.projectType = ProjectType.HostedClient;
        }

        return pi;
      }).ToArray();
    }

    private static int Info() {
      Version();
      Console.WriteLine();
      return DotNet("--info");
    }

    private static int Version() {
      var informationlVersion = ((AssemblyInformationalVersionAttribute) Attribute.GetCustomAttribute(
          Assembly.GetExecutingAssembly(), typeof(AssemblyInformationalVersionAttribute), false))
        .InformationalVersion;
      Console.WriteLine($"🤖 Bionic v{informationlVersion}");
      return 0;
    }

    private static int DotNet(string cmd) {
      var watcher = Process.Start(DotNetExe.FullPathOrDefault(), cmd);
      watcher?.WaitForExit();
      return watcher?.ExitCode ?? 1;
    }

    private static Process OpenUrl(string url) {
      try {
        return Process.Start(url);
      }
      catch {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
          url = url.Replace("&", "^&");
          return Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") {CreateNoWindow = true});
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
          return Process.Start("xdg-open", url);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
          return Process.Start("open", url);
        }
      }

      return null;
    }

    private static bool FileContains(string path, string match) => File.ReadAllText(path).Contains(match);
  }
}